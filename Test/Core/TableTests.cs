/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// The handle tests of LuaScript (docs/design/core-luascript.md, sections 2.5,
// 2.9 and 7.2; milestone 8): LuaTableRef reads and writes by every key form,
// LuaKey, Pairs and IPairs (with the allocation probe), handle lifetimes and
// generations, LuaFunctionRef, Globals, NewEnvironment, Compile, the env
// overloads of Load and Register, borrowed handles of host functions, Retain,
// ErrorValue and ToDictionary. Run by Test/Native/run_core.py with the other
// Test/Core files.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using XLua;

public static partial class CoreTests
{
    const string TableFunctions = @"
config = { name = 'gold', rate = 2.5, count = 7, whole = 3.0, half = 3.5, on = true, off = false, big = math.maxinteger,
           bytes = '\0\1\255', nested = { inner = { deep = 42 } }, list = { 10, 20, 30 }, f = function(x) return x * 2 end,
           huge = 1e300, neg = -2147483649 }
guarded = setmetatable({}, { __index = function() error('__index ran') end, __newindex = function() error('__newindex ran') end })
function apply(f, ...) return f(...) end
function make(n) local t = {} for i = 1, n do t['k' .. i] = i end return t end
function sum(t) local s = 0 for _, v in pairs(t) do s = s + v end return s end
function get(t, k) return t[k] end
function keys(t) local n = 0 for _ in pairs(t) do n = n + 1 end return n end
function adder(a) return function(b) return a + b end end
function rawlength(t) return rawlen(t) end
";

    static LuaScript WithTables(LuaOptions options = null)
    {
        var lua = new LuaScript(options ?? new LuaOptions());
        lua.Load(TableFunctions, "tables");
        return lua;
    }

    public static void TestTableReads()
    {
        using (var lua = WithTables())
        using (LuaTableRef cfg = lua.Globals.GetTable("config"))
        {
            Equal("gold", cfg.GetString("name"), "GetString");
            Equal(2.5, cfg.GetDouble("rate"), "GetDouble");
            Equal(7L, cfg.GetLong("count"), "GetLong");
            Equal(7.0, cfg.GetDouble("count"), "an integer as double");
            Equal(3L, cfg.GetLong("whole"), "an integral float as long");
            Throws<LuaTypeException>(() => cfg.GetLong("half"), "a fraction is no integer");
            Throws<LuaTypeException>(() => cfg.GetLong("name"), "a string is not converted");
            Throws<LuaTypeException>(() => cfg.Get<int>("big"), "int range");
            Throws<LuaTypeException>(() => cfg.Get<float>("huge"), "float range");
            Equal(long.MaxValue, cfg.GetLong("big"), "64-bit integers stay lossless");
            Equal(-2147483649L, cfg.Get<long>("neg"), "below int");
            Check(cfg.GetBool("on") && !cfg.GetBool("off"), "GetBool");
            Check(cfg.Get<bool>("count"), "Lua's truth");
            byte[] bytes = cfg.GetBytes("bytes");
            Check(bytes.Length == 3 && bytes[0] == 0 && bytes[2] == 255, "GetBytes");
            Throws<KeyNotFoundException>(() => cfg.GetLong("missing"), "a missing key");
            Throws<KeyNotFoundException>(() => cfg.GetString("missing"), "a missing string key");
            Equal(5L, cfg.GetLong("missing", 5), "fallback");
            Equal(7L, cfg.GetLong("count", 5), "fallback not used");
            Equal("x", cfg.GetString("missing", "x"), "string fallback");
            Equal(true, cfg.GetBool("missing", true), "bool fallback");
            Throws<LuaTypeException>(() => cfg.GetLong("name", 5), "a fallback does not hide a wrong type");
            long l;
            Check(cfg.TryGetLong("count", out l) && l == 7, "TryGetLong");
            Check(!cfg.TryGetLong("missing", out l) && l == 0, "TryGetLong of a missing key");
            Check(!cfg.TryGetLong("name", out l), "TryGetLong of a string");
            Check(!cfg.TryGetLong("half", out l), "TryGetLong of a fraction");
            string s;
            Check(cfg.TryGetString("name", out s) && s == "gold", "TryGetString");
            double d;
            Check(cfg.TryGetDouble("rate", out d) && d == 2.5, "TryGetDouble");
            Equal(LuaType.String, cfg.TypeOf("name"), "TypeOf string");
            Equal(LuaType.Integer, cfg.TypeOf("count"), "TypeOf integer");
            Equal(LuaType.Number, cfg.TypeOf("rate"), "TypeOf number");
            Equal(LuaType.Table, cfg.TypeOf("nested"), "TypeOf table");
            Equal(LuaType.Function, cfg.TypeOf("f"), "TypeOf function");
            Equal(LuaType.Nil, cfg.TypeOf("missing"), "TypeOf missing");
            Equal(0, lua.LiveHandles - 1, "TypeOf of a table makes no handle");
            Span<byte> small = stackalloc byte[2];
            Span<byte> room = stackalloc byte[8];
            int written;
            Check(cfg.TryCopyBytes("bytes", room, out written) && written == 3 && room[1] == 1, "TryCopyBytes");
            Check(!cfg.TryCopyBytes("bytes", small, out written) && written == 3, "TryCopyBytes: too small gives the length");
            Check(!cfg.TryCopyBytes("count", room, out written) && written == 0, "TryCopyBytes of a number");
            using (LuaTableRef list = cfg.GetTable("list"))
            {
                Equal(3L, list.Length, "Length");
                Equal(20L, list.GetLong(2), "GetLong(index)");
                Equal(30L, list.Get<long>(3L), "Get<long>(index)");
                Equal(30L, list.Get((LuaValue)3).AsLong(), "a LuaValue key");
                Equal(30L, list.Get((LuaValue)3.0).AsLong(), "a float key finds the integer");
                Equal(LuaType.Nil, list.TypeOf(4), "past the end");
            }
            Equal(LuaType.Integer, cfg.Get((LuaValue)"count").Type, "Get(LuaValue)");
            Check(cfg.Get((LuaValue)"missing").IsNil, "Get(LuaValue) of a missing key is nil");
            Check(cfg.Get(LuaValue.Nil).IsNil, "a nil key reads nil");
            Check(cfg.Get(double.NaN).IsNil, "a NaN key reads nil");
            using (LuaTableRef nested = cfg.GetTable("nested"))
            using (LuaTableRef inner = nested.GetTable("inner"))
            {
                Equal(42L, inner.GetLong("deep"), "nested tables");
                Equal(3, lua.LiveHandles, "config and two nested owning handles");
            }
            LuaTableRef t;
            Check(cfg.TryGetTable("nested", out t) && t.IsValid, "TryGetTable");
            t.Dispose();
            Check(!cfg.TryGetTable("name", out t) && !t.IsValid, "TryGetTable of a string");
            Throws<LuaTypeException>(() => cfg.GetTable("name"), "GetTable of a string");
            Equal(1, lua.LiveHandles, "a failed read leaves no handle");
            using (LuaFunctionRef f = cfg.GetFunction("f"))
            {
                Equal(14L, f.Call<long>(7), "GetFunction");
            }
            using (LuaTableRef guarded = lua.Globals.GetTable("guarded"))
            {
                Check(guarded.Get((LuaValue)"x").IsNil, "reads are raw: no __index");
                guarded.Set("x", 1);
                Equal(1L, guarded.GetLong("x"), "writes are raw: no __newindex");
            }
        }
    }

    public static void TestTableWrites()
    {
        using (var lua = WithTables())
        using (LuaTableRef t = lua.NewTable(4, 4))
        {
            t.Set("s", "text");
            t.Set("i", 42);
            t.Set("n", 0.5);
            t.Set("b", true);
            t.Set("u", LuaValue.FromBytes(new byte[] { 0, 1, 2 }));
            t.Set(1, "one");
            t.Set(2L, 2);
            t.Set((LuaValue)true, "a boolean key");
            t.Set((LuaValue)1.5, "a float key");
            LuaKey key = lua.Key("k");
            t.Set(key, "by key");
            Equal("text", t.GetString("s"), "string value");
            Equal(42L, t.GetLong("i"), "integer value");
            Equal(0.5, t.GetDouble("n"), "float value");
            Check(t.GetBool("b"), "bool value");
            Equal(3, t.GetBytes("u").Length, "bytes value");
            Equal("one", t.GetString(1), "integer key");
            Equal("a boolean key", t.Get((LuaValue)true).AsString(), "a boolean key");
            Equal("a float key", t.Get((LuaValue)1.5).AsString(), "a float key");
            Equal("by key", t.GetString("k"), "a LuaKey writes the same key");
            Equal("by key", t.Get<string>(key), "and reads it");
            Equal(2L, t.Length, "Length");
            Equal("text", lua.Call<string>("get", t, "s"), "a table as an argument");
            using (LuaTableRef inner = t.SetNewTable("inner", 0, 2))
            {
                inner.Set("x", 1);
            }
            using (LuaTableRef inner = t.GetTable("inner"))
            {
                Equal(1L, inner.GetLong("x"), "SetNewTable");
            }
            using (LuaFunctionRef f = lua.Globals.GetFunction("adder"))
            {
                t.Set("f", f);
            }
            Equal(LuaType.Function, t.TypeOf("f"), "a function value");
            t.Set("s", LuaValue.Nil);
            Equal(LuaType.Nil, t.TypeOf("s"), "nil removes a key");
            Throws<LuaRuntimeException>(() => t.Set(LuaValue.Nil, 1), "a nil key");
            Throws<LuaRuntimeException>(() => t.Set(double.NaN, 1), "a NaN key");
            Throws<ArgumentNullException>(() => t.Set((string)null, 1), "a null string key");
            using (var co = new LuaValueHolder(lua.Load<LuaValue>("return coroutine.create(function() end)", "co")))
            {
                Throws<ArgumentException>(() => t.Set("co", co.Value), "a coroutine cannot be set");
            }
            lua.Globals.Set("fromCs", t);
            Equal("one", lua.Load<string>("return fromCs[1]", "globals"), "Globals.Set");
            using (LuaTableRef back = lua.Globals.GetTable("fromCs"))
            {
                Check(back == t, "and back as the same table");
            }
        }
    }

    // A LuaValue in a using block (LuaValue.Dispose releases its handle).
    struct LuaValueHolder : IDisposable
    {
        public readonly LuaValue Value;
        public LuaValueHolder(LuaValue v) { Value = v; }
        public void Dispose() { Value.Dispose(); }
    }

    public static void TestTableLength()
    {
        using (var lua = WithTables())
        {
            using (LuaTableRef t = lua.Load<LuaTableRef>("return {1, 2, 3, nil, 5}", "holes"))
            {
                long n = t.Length;
                Check(n == 5 || n == 3, "a border of a table with a hole: " + n);
                Equal(lua.Call<long>("rawlength", t), n, "as rawlen");
            }
            using (LuaTableRef t = lua.NewTable())
            {
                Equal(0L, t.Length, "empty");
                for (int i = 1; i <= 100; i++) t.Set(i, i);
                Equal(100L, t.Length, "100");
                t.Set("x", 1);
                Equal(100L, t.Length, "hash keys do not count");
            }
        }
    }

    public static void TestTablePairs()
    {
        using (var lua = WithTables())
        using (LuaTableRef cfg = lua.Globals.GetTable("config"))
        {
            // The same order as Lua's own next.
            string expected = lua.Load<string>("local s = {} for k in pairs(config) do s[#s + 1] = tostring(k) end return table.concat(s, ',')", "order");
            var seen = new List<string>();
            int strings = 0, tables = 0;
            foreach (LuaPair p in cfg.Pairs())
            {
                seen.Add(p.Key.ToString());
                if (p.KeyType == LuaType.String) strings++;
                if (p.ValueType == LuaType.Table)
                {
                    tables++;
                    LuaTableRef borrowed = p.Value.AsTable();
                    Check(borrowed.IsValid, "a table value is a borrowed handle");
                }
                if (p.KeyEquals("count"))
                {
                    Equal(7L, p.Value.AsLong(), "KeyEquals and the value");
                    Equal("count", Encoding.UTF8.GetString(p.KeyUtf8.ToArray()), "KeyUtf8");
                }
                if (p.KeyEquals("name"))
                {
                    Equal("gold", Encoding.UTF8.GetString(p.ValueUtf8.ToArray()), "ValueUtf8");
                }
            }
            Equal(expected, string.Join(",", seen), "the order of Lua's next");
            Equal(seen.Count, strings, "string keys");
            Equal(2, tables, "two table values");
            Equal(1, lua.LiveHandles, "borrowed handles and the finished iteration do not count");

            // A borrowed value ends with its step; Retain keeps it.
            LuaTableRef kept = default(LuaTableRef), stale = default(LuaTableRef);
            LuaPair old = default(LuaPair);
            bool more = false;
            LuaPairs e = cfg.Pairs();  // not in a using: a using variable is read-only, and MoveNext changes it
            try
            {
                while (e.MoveNext())
                {
                    if (e.Current.KeyEquals("nested"))
                    {
                        stale = e.Current.Value.AsTable();
                        kept = stale.Retain();
                        old = e.Current;
                        more = e.MoveNext();  // false when "nested" was the last key (the order depends on the seed)
                        break;
                    }
                }
                Check(!stale.IsValid, "a borrowed table ends with its step");
                Throws<ObjectDisposedException>(() => stale.GetTable("inner"), "a stale borrowed handle");
                Throws<InvalidOperationException>(() => { var k = old.Key; }, "a pair of an earlier step");
                Equal(more ? 3 : 2, lua.LiveHandles, "an unfinished iteration counts, a finished one not");
            }
            finally
            {
                e.Dispose();
            }
            Equal(2, lua.LiveHandles, "Dispose ends it");
            using (kept)
            using (LuaTableRef inner = kept.GetTable("inner"))
            {
                Equal(42L, inner.GetLong("deep"), "the retained table");
            }
            Equal(1, lua.LiveHandles, "back to config");

            // Manual iteration to the end releases the iterator without Dispose.
            LuaPairs manual = cfg.Pairs();
            int count = 0;
            while (manual.MoveNext()) count++;
            Check(!manual.MoveNext(), "stays at the end");
            Equal(seen.Count, count, "manual MoveNext");
            Equal(1, lua.LiveHandles, "released at the end");

            // Nested iterations.
            int pairsOfPairs = 0;
            using (LuaTableRef list = cfg.GetTable("list"))
            {
                foreach (LuaPair a in list.Pairs())
                    foreach (LuaPair b in list.Pairs())
                        pairsOfPairs += (int)(a.Value.AsLong() / 10 * b.Value.AsLong() / 10);
            }
            Equal(36, pairsOfPairs, "nested iterations");

            // Assigning existing keys during the traversal is fine.
            using (LuaTableRef t = lua.Call<LuaTableRef>("make", 50))
            {
                foreach (LuaPair p in t.Pairs()) t.Set(p.Key, p.Value.AsLong() * 2);
                Equal(2550L, lua.Call<long>("sum", t), "assigned during traversal");
                // Removing the current key and adding many keys rehashes the table: Lua's error.
                var error = Throws<LuaRuntimeException>(() =>
                {
                    int added = 0;
                    foreach (LuaPair p in t.Pairs())
                    {
                        t.Set(p.Key, LuaValue.Nil);
                        for (int i = 0; i < 200; i++) t.Set("new" + added++, i);
                    }
                }, "adding keys during traversal");
                if (error != null) Contains(error.Message, "invalid key to 'next'", "Lua's message");
            }
            Equal(1, lua.LiveHandles, "a failed iteration is released by foreach");

            // A table key and a function value.
            using (LuaTableRef t = lua.Load<LuaTableRef>("local k = {} return { [k] = print or tostring }", "tablekey"))
            {
                foreach (LuaPair p in t.Pairs())
                {
                    Equal(LuaType.Table, p.KeyType, "a table key");
                    Equal(LuaType.Function, p.ValueType, "a function value");
                    Check(p.Key.AsTable().IsValid && p.Value.AsFunction().IsValid, "borrowed key and value");
                    Contains(p.Value.AsFunction().Call<string>(p.Key), "table: ", "a borrowed function called with a borrowed key");
                }
            }

            // IPairs: 1..n up to the first nil.
            using (LuaTableRef t = lua.Load<LuaTableRef>("return {1, 2, 3, nil, 5, x = 1}", "ipairs"))
            {
                long total = 0, last = 0;
                foreach (LuaPair p in t.IPairs())
                {
                    Equal(LuaType.Integer, p.KeyType, "integer keys");
                    total += p.Value.AsLong();
                    last = p.Key.AsLong();
                }
                Equal(6L, total, "up to the first nil");
                Equal(3L, last, "the last index");
            }
            int none = 0;
            using (LuaTableRef empty = lua.NewTable())
            {
                foreach (LuaPair p in empty.Pairs()) none++;
                foreach (LuaPair p in empty.IPairs()) none++;
            }
            Equal(0, none, "an empty table");
            Equal(1, lua.LiveHandles, "no iteration left");
            // Disposing the table during its iteration: the next step throws, and foreach releases the iterator.
            Throws<ObjectDisposedException>(() =>
            {
                LuaTableRef list = cfg.GetTable("list");
                foreach (LuaPair p in list.Pairs()) list.Dispose();
            }, "the table is disposed during its iteration");
            Equal(1, lua.LiveHandles, "nothing left");
        }
    }

    public static void TestTablePairsAllocateNothing()
    {
        long probe = Allocated();
        var keep = new byte[1000];
        if (Allocated() - probe < 1000)
        {
            Console.WriteLine("  GC.GetAllocatedBytesForCurrentThread does not count on this runtime; skipped");
            return;
        }
        using (var lua = WithTables())
        using (LuaTableRef t = lua.Call<LuaTableRef>("make", 10000))
        using (LuaTableRef cfg = lua.Globals.GetTable("config"))
        {
            LuaKey count = lua.Key("count");
            for (int round = 0; round < 2; round++)
            {
                long before = Allocated();
                long sum = 0;
                int hits = 0;
                foreach (LuaPair p in t.Pairs())
                {
                    sum += p.Value.AsLong();
                    if (p.KeyEquals("k5000")) hits++;
                    if (p.KeyType != LuaType.String) hits += 100;
                }
                foreach (LuaPair p in cfg.Pairs())
                {
                    if (p.ValueType == LuaType.Table) sum += p.Value.AsTable().Length;  // a borrowed table
                }
                for (int i = 0; i < 1000; i++)
                {
                    sum += cfg.GetLong("count") + cfg.GetLong(count) + (long)cfg.GetDouble("rate");
                    cfg.Set(count, 7);
                    cfg.Set("count", 7);
                    if (cfg.TypeOf("count") != LuaType.Integer) hits += 1000;
                    sum += cfg.Length;
                }
                long allocated = Allocated() - before;
                if (round == 1)
                {
                    Console.WriteLine("  allocation probe: " + allocated + " bytes for Pairs over 10,000 keys and 6,000 typed reads and writes");
                    Equal(0L, allocated, "bytes allocated by Pairs and the typed getters and setters");
                }
                Equal(50005000L + 3 + 1000L * (7 + 7 + 2), sum, "the sum");
                Equal(1, hits, "one key matched");
            }
        }
        GC.KeepAlive(keep);
    }

    public static void TestTableHandles()
    {
        LuaTableRef afterDispose;
        LuaFunctionRef functionAfter;
        using (var lua = WithTables())
        {
            Equal(0, lua.LiveHandles, "none at first");
            LuaTableRef a = lua.Globals.GetTable("config");
            LuaTableRef copy = a;
            LuaTableRef b = lua.Globals.GetTable("config");
            Equal(2, lua.LiveHandles, "two owning handles");
            Check(a == b && a.Equals(b) && a.Equals((object)b), "two handles to one table are equal");
            Check(a.GetHashCode() == b.GetHashCode(), "and hash alike");
            Check(a != lua.Globals, "another table");
            a.Dispose();
            Equal(1, lua.LiveHandles, "Dispose");
            Check(!copy.IsValid, "a copy of a disposed handle is invalid");
            Throws<ObjectDisposedException>(() => copy.GetLong("count"), "a copy of a disposed handle throws");
            a.Dispose();
            copy.Dispose();
            Equal(1, lua.LiveHandles, "a second Dispose does nothing");
            LuaTableRef c = lua.NewTable();  // reuses a's slot with a new generation
            Check(c.IsValid && !copy.IsValid, "a reused slot does not revive the stale copy");
            Throws<ObjectDisposedException>(() => copy.Set("x", 1), "still throws");
            c.Dispose();
            Check(a != b, "an invalid handle equals only its own copies");
            Check(a == copy, "copies are equal");
            b.Dispose();
            Equal(0, lua.LiveHandles, "all released");

            LuaTableRef none = default(LuaTableRef);
            Check(!none.IsValid && none.Script == null, "default(LuaTableRef)");
            Throws<ObjectDisposedException>(() => none.GetLong("x"), "default(LuaTableRef) throws");
            none.Dispose();
            Check(((LuaValue)none).IsNil, "default(LuaTableRef) is nil as a value");

            // Globals is never disposed.
            LuaTableRef g = lua.Globals;
            g.Dispose();
            Check(lua.Globals.IsValid && g.IsValid, "Globals stays valid");
            Equal(0, lua.LiveHandles, "Globals does not count");

            // Handles of another script.
            using (var other = WithTables())
            using (LuaTableRef foreign = other.Globals.GetTable("config"))
            {
                Throws<ArgumentException>(() => lua.Call("get", foreign, "name"), "a table of another script as an argument");
                Throws<ArgumentException>(() => lua.Globals.Set("x", foreign), "as a value");
                Throws<ArgumentException>(() => lua.Load("x = 1", "env", foreign), "as an environment");
                Throws<ArgumentException>(() => lua.Globals.GetLong(other.Key("count")), "a LuaKey of another script");
                Check(foreign != lua.Globals.GetTable("config"), "tables of two scripts differ");
            }
            Equal(1, lua.LiveHandles, "the GetTable above leaked one handle, as documented");

            // Many handles: the slot table grows and reuses.
            var many = new LuaTableRef[1000];
            for (int i = 0; i < many.Length; i++) many[i] = lua.NewTable();
            Equal(1001, lua.LiveHandles, "1,000 handles");
            for (int i = 0; i < many.Length; i++) many[i].Dispose();
            Equal(1, lua.LiveHandles, "released");
            long used = lua.MemoryUsed;
            for (int i = 0; i < 10000; i++) lua.NewTable().Dispose();
            lua.CollectGarbage();
            Check(lua.MemoryUsed < used + 64 * 1024, "released tables are collected");

            afterDispose = lua.Globals.GetTable("config");
            functionAfter = lua.Globals.GetFunction("apply");
        }
        // After the script is disposed: every member throws except IsValid and Dispose.
        Check(!afterDispose.IsValid && !functionAfter.IsValid, "handles are invalid after the script is disposed");
        Throws<ObjectDisposedException>(() => afterDispose.GetLong("count"), "a table after the script");
        Throws<ObjectDisposedException>(() => { var n = afterDispose.Length; }, "Length after the script");
        Throws<ObjectDisposedException>(() => afterDispose.Pairs(), "Pairs after the script");
        Throws<ObjectDisposedException>(() => functionAfter.Call(), "a function after the script");
        afterDispose.Dispose();
        functionAfter.Dispose();
        Throws<ObjectDisposedException>(() => { var g = new LuaScript(new LuaOptions()); g.Dispose(); var x = g.Globals; }, "Globals after Dispose");
    }

    public static void TestTableToDictionary()
    {
        using (var lua = WithTables())
        {
            using (LuaTableRef cfg = lua.Globals.GetTable("config"))
            {
                Dictionary<string, object> d = cfg.ToDictionary();
                Equal("gold", (string)d["name"], "string");
                Equal(7L, (long)d["count"], "boxed long");
                Equal(2.5, (double)d["rate"], "boxed double");
                Equal(true, (bool)d["on"], "boxed bool");
                Equal("function", (string)d["f"], "a function by its type name");
                var list = (List<object>)d["list"];
                Check(list.Count == 3 && (long)list[2] == 30, "a sequence becomes a List<object>");
                var nested = (Dictionary<string, object>)d["nested"];
                Equal(42L, (long)((Dictionary<string, object>)nested["inner"])["deep"], "nested");
                Throws<InvalidOperationException>(() => cfg.ToDictionary(2), "deeper than maxDepth");
                Equal(1, lua.LiveHandles, "no handle left");
            }
            using (LuaTableRef t = lua.Load<LuaTableRef>("local t = {1, 2, x = {}} t.x.back = t return t", "cycle"))
            {
                var e = Throws<InvalidOperationException>(() => t.ToDictionary(), "a cycle");
                if (e != null) Contains(e.Message, "cycle", "the message names the cycle");
            }
            using (LuaTableRef t = lua.Load<LuaTableRef>("local s = {} return {a = s, b = s, [1.5] = 1, [true] = 2, [3] = 3}", "shared"))
            {
                Dictionary<string, object> d = t.ToDictionary();
                Check(d.ContainsKey("a") && d.ContainsKey("b"), "a table seen twice is no cycle");
                Check(d.ContainsKey("1.5") && d.ContainsKey("true") && d.ContainsKey("3"), "number and boolean keys as text");
            }
            using (LuaTableRef t = lua.Load<LuaTableRef>("return {[{}] = 1}", "tablekey"))
            {
                Throws<InvalidOperationException>(() => t.ToDictionary(), "a table key");
            }
            Equal(0, lua.LiveHandles, "no handle left");
        }
    }

    public static void TestKeys()
    {
        using (var lua = WithTables())
        using (LuaTableRef cfg = lua.Globals.GetTable("config"))
        {
            LuaKey k = lua.Key("count");
            Check(k.IsValid && k.Name == "count" && k.Script == lua, "a key");
            Check(lua.Key("count").Equals(k), "the same name gives the same key");
            Equal(7L, cfg.GetLong(k), "GetLong(LuaKey)");
            Equal(7L, cfg.Get(k, 0L), "Get(LuaKey, fallback)");
            Equal(5L, cfg.Get(lua.Key("missing"), 5L), "a missing key");
            Equal(7.0, cfg.GetDouble(k), "GetDouble(LuaKey)");
            LuaKey unicode = lua.Key("名前");
            cfg.Set(unicode, "x");
            Equal("x", cfg.GetString("名前"), "a UTF-8 key");
            Equal(1, lua.LiveHandles, "keys are not handles");
            Throws<ArgumentException>(() => cfg.GetLong(default(LuaKey)), "default(LuaKey)");
            // Many lookups of the same name make no new native key.
            long before = lua.MemoryUsed;
            for (int i = 0; i < 1000; i++) lua.Key("count");
            Check(lua.MemoryUsed <= before + 1024, "keys are cached");
        }
    }

    public static void TestFunctionRefs()
    {
        using (var lua = WithTables())
        {
            using (LuaFunctionRef add = lua.Globals.GetFunction("adder"))
            using (LuaFunctionRef plus5 = add.Call<LuaFunctionRef>(5))
            {
                Equal(8L, plus5.Call<long>(3), "a function result is a handle");
                Equal(8.5, plus5.Call<double>(3.5), "Call<double>");
                Equal(9L, lua.Call<long>("apply", plus5, 4), "a function as an argument");
                LuaFunctionRef again = plus5.Retain();
                Check(again == plus5 && again.Slot != plus5.Slot, "Retain is a new handle to the same function");
                Equal(3, lua.LiveHandles, "adder, plus5 and the retained copy");
                again.Dispose();
                Check(plus5.IsValid, "disposing the copy leaves the original");
            }
            Equal(0, lua.LiveHandles, "all disposed");
            using (LuaFunctionRef echo = lua.Load<LuaFunctionRef>("return function(...) return ... end", "echo"))
            {
                Equal("x", echo.Call<string>("x"), "1 argument");
                Equal(4L, echo.Call<long>(4, 3, 2, 1), "4 arguments");
                var args = new LuaValue[] { 1, 2, 3, 4, 5, 6 };
                var results = new LuaValue[8];
                Equal(6, echo.Call(args, results), "a span of 6");
                Check(results[5].AsLong() == 6 && results[6].IsNil, "results");
                var (x, y) = echo.Call<long, string>(new LuaValue[] { 1, "two" });
                Check(x == 1 && y == "two", "a tuple");
                var (p, q, r) = echo.Call<long, long, bool>(new LuaValue[] { 1, 2 });
                Check(p == 1 && q == 2 && !r, "a triple with a missing value");
                echo.Call();
                echo.Call(ReadOnlySpan<LuaValue>.Empty);
            }
            using (LuaFunctionRef fail = lua.Load<LuaFunctionRef>("return function() error('boom') end", "fail"))
            {
                var e = Throws<LuaRuntimeException>(() => fail.Call(), "an error");
                if (e != null) Contains(e.Message, "fail:1: boom", "the position");
            }
            using (LuaFunctionRef spin = lua.Load<LuaFunctionRef>("return function() while true do end end", "spin"))
            {
                lua.InstructionLimit = 100000;
                Throws<LuaInstructionLimitException>(() => spin.Call(), "the limits apply");
                lua.InstructionLimit = 0;
            }
            Throws<LuaTypeException>(() => lua.Call<LuaFunctionRef>("adder", 1).Call<LuaFunctionRef>(1), "a number is no function");
            Equal(1, lua.LiveHandles, "the failed read released its handle; the leaked adder result counts");
        }
    }

    public static void TestEnvironments()
    {
        using (var lua = WithTables())
        {
            // Globals is the default environment.
            lua.Globals.Set("fromHost", 5);
            Equal(5L, lua.Load<long>("return fromHost", "g"), "Globals.Set is a global");
            lua.Load("fromScript = 'yes'", "g2");
            Equal("yes", lua.Globals.GetString("fromScript"), "a global is in Globals");
            Equal(LuaType.Table, lua.Globals.TypeOf("string"), "Globals has its own copy of string");
            Equal(LuaType.Nil, lua.Globals.TypeOf("pairs"), "the base functions are in the frozen base, not raw in Globals");
            using (LuaTableRef env = lua.NewEnvironment())
            {
                lua.Load("x = 1; function inEnv() return x + (fromHost or 100) end", "env", env);
                Equal(1L, env.GetLong("x"), "a global of the environment");
                Equal(LuaType.Nil, lua.Globals.TypeOf("x"), "not in Globals");
                Equal(101L, env.Call<long>("inEnv"), "LuaTableRef.Call in its environment");
                Equal(2L, lua.Load<long>("return x + 1", "env2", env), "Load<T> with env");
                Check(lua.Load<bool>("return _G == _ENV and string.format ~= nil and setmetatable ~= nil", "shape", env), "the environment's shape");
                var results = new LuaValue[2];
                Equal(2, lua.Load("return x, 'two'", "env3", env, results), "Load with env and results");
                Throws<LuaRuntimeException>(() => lua.Call("inEnv"), "not a global of Globals");
            }
            // Compile without running, then run twice.
            using (LuaFunctionRef counter = lua.Compile("n = (n or 0) + 1 return n", "counter"))
            {
                Equal(LuaType.Nil, lua.Globals.TypeOf("n"), "Compile does not run the chunk");
                Equal(1L, counter.Call<long>(), "first run");
                Equal(2L, counter.Call<long>(), "second run");
            }
            using (LuaTableRef env = lua.NewEnvironment())
            using (LuaFunctionRef inEnv = lua.Compile("y = ... return y", "compiled", env))
            {
                Equal(7L, inEnv.Call<long>(7), "a compiled chunk gets its arguments");
                Equal(7L, env.GetLong("y"), "and its environment");
            }
            var syntax = Throws<LuaSyntaxException>(() => lua.Compile("return +", "bad"), "Compile of a syntax error");
            if (syntax != null) Equal("bad", syntax.ChunkName, "the chunk name");
            Throws<LuaSyntaxException>(() => lua.Compile("\x1bLua", "binary"), "Compile refuses bytecode");
            // A plain table as the environment: the chunk sees only it.
            using (LuaTableRef plain = lua.NewTable())
            {
                plain.Set("v", 3);
                Equal(3L, lua.Load<long>("return v", "plain", plain), "a plain table");
                Throws<LuaRuntimeException>(() => lua.Load("return tostring(v)", "plain2", plain), "no base functions there");
            }
            // Register into an environment.
            using (LuaTableRef env = lua.NewEnvironment())
            {
                lua.Register(env, "envOnly", (ref LuaCall c) => c.Return(c.GetLong(0) * 3));
                lua.Register(env, "dotted.name", (ref LuaCall c) => c.Return("dotted"));
                Equal(9L, lua.Load<long>("return envOnly(3)", "reg", env), "a host function in an environment");
                Equal("dotted", lua.Load<string>("return _ENV['dotted.name']()", "reg2", env), "a dotted name is a plain key there");
                Equal(LuaType.Nil, lua.Globals.TypeOf("envOnly"), "not in Globals");
                Throws<LuaRuntimeException>(() => lua.Load("return envOnly(3)", "reg3"), "not seen from Globals");
                lua.Register(env, "envOnly", (ref LuaCall c) => c.Return(-1L));
                Equal(-1L, lua.Load<long>("return envOnly(3)", "reg4", env), "replaced");
                lua.Unregister(env, "envOnly");
                Equal(LuaType.Nil, env.TypeOf("envOnly"), "Unregister(env)");
            }
            lua.Register(lua.Globals, "onGlobals", (ref LuaCall c) => c.Return(1L));
            Check(lua.Load<bool>("return rawget(_G, 'onGlobals') ~= nil", "rawget"), "Register(Globals) sets the Globals table itself");
            Equal(LuaType.Function, lua.Globals.TypeOf("onGlobals"), "visible from C#");
            using (LuaTableRef env = lua.NewEnvironment())
            {
                Equal(LuaType.Nil, lua.Load<LuaValue>("return onGlobals", "other", env).Type, "but not in another environment");
            }
            Equal(0, lua.LiveHandles, "no handle left");
        }
    }

    public static void TestErrorValue()
    {
        using (var lua = WithTables())
        {
            var e = Throws<LuaRuntimeException>(() => lua.Load("error({code = 5, why = 'x'})", "t"), "error with a table");
            if (e != null)
            {
                Equal(LuaType.Table, e.ErrorValue.Type, "ErrorValue is the table");
                Equal(5L, e.ErrorValue.AsTable().GetLong("code"), "its fields");
                Equal(1, lua.LiveHandles, "an owning handle the exception holds");
                e.ErrorValue.Dispose();
                Equal(0, lua.LiveHandles, "disposed");
                Contains(e.Message, "table value", "the message describes it");
            }
            e = Throws<LuaRuntimeException>(() => lua.Load("error('plain')", "s"), "error with a string");
            if (e != null) Check(e.ErrorValue.Type == LuaType.String && e.ErrorValue.AsString() == e.Message, "a string error is the message");
            e = Throws<LuaRuntimeException>(() => lua.Load("error(function() end)", "f"), "error with a function");
            if (e != null)
            {
                Equal(LuaType.Function, e.ErrorValue.Type, "a function");
                e.ErrorValue.Dispose();
            }
            e = Throws<LuaRuntimeException>(() => lua.Load("error(true)", "b"), "error with a boolean");
            if (e != null) Equal(LuaType.String, e.ErrorValue.Type, "a boolean error is described");
            // A table error that the script catches is no handle.
            Equal(5L, lua.Load<long>("local ok, t = pcall(error, {code = 5}) return t.code", "caught"), "caught in Lua");
            lua.InstructionLimit = 10000;
            Throws<LuaInstructionLimitException>(() => lua.Load("while true do end", "spin"), "a limit error");
            lua.InstructionLimit = 0;
            lua.MemoryLimit = lua.MemoryUsed + (256 << 10);
            Throws<LuaException>(() => lua.Load("error(setmetatable({}, {__tostring = function() local t = {} while true do t[#t + 1] = {} end end}))", "mem"), "a memory error while describing a table error");
            lua.MemoryLimit = 0;
            lua.CollectGarbage();
            Equal(0, lua.LiveHandles, "no handle leaks from other errors");
        }
    }

    public static void TestBorrowedHandles()
    {
        using (var lua = WithTables())
        {
            LuaTableRef escaped = default(LuaTableRef);
            LuaTableRef retained = default(LuaTableRef);
            int liveInside = -1;
            lua.Register("inspect", (ref LuaCall c) =>
            {
                LuaTableRef t = c.GetTable(0);
                Check(t.IsValid, "a borrowed table");
                t.Set("seen", true);
                long n = 0;
                foreach (LuaPair p in t.Pairs()) n++;
                escaped = t;
                retained = t.Retain();
                liveInside = c.Script.LiveHandles;
                LuaFunctionRef f = c.GetFunction(1);
                c.Return(f.Call<long>(n));
                LuaValue v = c.Get(0);
                Check(v.Type == LuaType.Table && v.AsTable() == t, "Get(i) of a table is a borrowed handle too");
            });
            Equal(25L, lua.Load<long>("local t = {1, 2} t.x = 1 return inspect(t, function(n) return n * 5 + 5 end)", "borrow"), "GetTable, Pairs and GetFunction inside");
            Equal(1, liveInside, "borrowed handles do not count; the retained one does");
            Check(!escaped.IsValid, "a borrowed handle ends with the host function");
            Throws<ObjectDisposedException>(() => escaped.GetBool("seen"), "using it later throws");
            Check(retained.GetBool("seen"), "the retained handle sees the write");
            retained.Dispose();
            Equal(0, lua.LiveHandles, "released");

            // A borrowed table returned as the result: it is pushed before its reference goes.
            lua.Register("same", (ref LuaCall c) => c.Return(c.GetTable(0)));
            Check(lua.Load<bool>("local t = {} return same(t) == t", "same"), "a borrowed argument as the result");
            lua.Register("sameValue", (ref LuaCall c) => c.Return(c.Get(0)));
            Check(lua.Load<bool>("local f = print or tostring return sameValue(f) == f", "samef"), "a borrowed function through Get");

            // Wrong types.
            lua.Register("needsTable", (ref LuaCall c) => c.GetTable(0));
            var e = Throws<LuaRuntimeException>(() => lua.Load("needsTable(1)", "wrong"), "a number for GetTable");
            if (e != null) Contains(e.Message, "bad argument #1 to 'needsTable' (table expected, got number)", "Lua's message");
            e = Throws<LuaRuntimeException>(() => lua.Load("needsTable()", "none"), "no argument");
            if (e != null) Contains(e.Message, "table expected, got no value", "no value");

            // Typed shorthands with handles.
            lua.Register<LuaTableRef, long>("count", t => { long n = 0; foreach (LuaPair p in t.Pairs()) n++; return n; });
            Equal(3L, lua.Load<long>("return count({1, 2, x = 3})", "typed"), "Register<LuaTableRef, long>");
            lua.Register<LuaFunctionRef, long, long>("call", (f, x) => f.Call<long>(x));
            Equal(6L, lua.Load<long>("return call(function(x) return x * 2 end, 3)", "typedf"), "Register<LuaFunctionRef, long, long>");
            lua.Register<LuaTableRef>("makeTable", () => { var t = lua.NewTable(); t.Set("made", 1); return t; });
            Equal(1L, lua.Load<long>("return makeTable().made", "typedr"), "a LuaTableRef result");
            Equal(1, lua.LiveHandles, "the result handle stays the host's (leaked here on purpose)");

            // Nested host functions keep their own borrowed handles.
            lua.Register("outer", (ref LuaCall c) =>
            {
                LuaTableRef t = c.GetTable(0);
                long inner = c.Script.Call<long>("innerCall", t);
                Check(t.IsValid, "the outer handle survives a nested host function");
                c.Return(inner + t.GetLong("v"));
            });
            lua.Register("innerTable", (ref LuaCall c) => c.Return(c.GetTable(0).GetLong("v") * 10));
            lua.Load("function innerCall(t) return innerTable(t) end", "nested");
            Equal(11L, lua.Load<long>("return outer({v = 1})", "nested2"), "nested borrowed handles");

            // Many borrowed handles in one call are released afterwards.
            lua.Register("borrowMany", (ref LuaCall c) => { for (int i = 0; i < 100; i++) c.GetTable(0); c.Return(1L); });
            long before = lua.MemoryUsed;
            lua.Load("local t = {} for i = 1, 100 do borrowMany(t) end", "many");
            lua.CollectGarbage();
            Check(lua.MemoryUsed < before + 32 * 1024, "the borrowed references are released");

            // A buffer argument as a handle, when the library is there.
            using (var b = OpenBuffer())
            {
                if (b != null)
                {
                    b.Register("fillFirst", (ref LuaCall c) => { LuaBufferRef buf = c.GetBuffer(0); buf.WriteByte(0, 7); c.Return(buf.Length); });
                    Equal(7L, b.Load<long>("local x = buffer.create(4) fillFirst(x) return buffer.readu8(x, 0) + fillFirst(x) - 4", "buf"), "GetBuffer");
                }
            }
        }
    }

    public static void TestTableMemoryAndThreads()
    {
        using (var lua = WithTables(new LuaOptions { MemoryLimit = 4 << 20 }))
        {
            using (LuaTableRef t = lua.NewTable())
            {
                var e = Throws<LuaMemoryLimitException>(() => { for (int i = 1; i < 10000000; i++) t.Set(i, i); }, "writes past the memory limit");
                if (e != null) Check(e.MemoryUsed <= (4 << 20) + 16 * 1024, "within the limit");
            }
            lua.CollectGarbage();
            lua.Register("growInside", (ref LuaCall c) =>
            {
                bool refused = false;
                using (LuaTableRef big = c.Script.NewTable())
                {
                    try
                    {
                        for (int i = 1; i < 10000000; i++) big.Set(i, i);
                    }
                    catch (LuaMemoryLimitException)
                    {
                        refused = true;
                    }
                }
                c.Script.CollectGarbage();
                c.Return(refused ? "refused inside" : "not refused");
            });
            Equal("refused inside", lua.Load<string>("return growInside()", "inside"), "the memory limit refuses inside a host function too");
            lua.CollectGarbage();
            Exception seen = null;
            var other = new Thread(() =>
            {
                try
                {
                    lua.Globals.GetTable("config");
                }
                catch (Exception x)
                {
                    seen = x;
                }
            });
            other.Start();
            other.Join();
            Check(seen is InvalidOperationException, "another thread must not use a handle (LuaOptions.CheckThread)");
            Equal(0, lua.LiveHandles, "no handle left");
        }
    }

    public static void TestTableCost()
    {
        using (var lua = WithTables())
        using (LuaTableRef cfg = lua.Globals.GetTable("config"))
        using (LuaTableRef big = lua.Call<LuaTableRef>("make", 10000))
        using (LuaFunctionRef echo = lua.Load<LuaFunctionRef>("return function(x) return x end", "echo"))
        {
            lua.Load("function echoGlobal(x) return x end", "echoGlobal");
            LuaKey count = lua.Key("count");
            const int N = 200000;
            double best(Func<double> f) { double m = double.MaxValue; for (int r = 0; r < 7; r++) m = Math.Min(m, f()); return m; }
            double perCall(Action body, int n)
            {
                var w = Stopwatch.StartNew();
                body();
                return w.Elapsed.TotalMilliseconds * 1e6 / n;
            }
            long sink = 0;
            double getString = best(() => perCall(() => { for (int i = 0; i < N; i++) sink += cfg.GetLong("count"); }, N));
            double getKey = best(() => perCall(() => { for (int i = 0; i < N; i++) sink += cfg.GetLong(count); }, N));
            double getIndex = best(() => perCall(() => { for (int i = 0; i < N; i++) sink += big.TypeOf(i) == LuaType.Nil ? 1 : 0; }, N));
            double set = best(() => perCall(() => { for (int i = 0; i < N; i++) cfg.Set(count, 7); }, N));
            double pairs = best(() => perCall(() => { foreach (LuaPair p in big.Pairs()) sink += p.Value.AsLong(); }, 10000));
            double callRef = best(() => perCall(() => { for (int i = 0; i < N; i++) sink += echo.Call<long>(i); }, N));
            double callName = best(() => perCall(() => { for (int i = 0; i < N; i++) sink += lua.Call<long>("echoGlobal", i); }, N));
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "  table cost (min of 7): GetLong(string) {0:F1} ns, GetLong(LuaKey) {1:F1} ns, TypeOf(long) {2:F1} ns, Set(LuaKey) {3:F1} ns, "
                + "Pairs {4:F1} ns per pair, LuaFunctionRef.Call<long> {5:F1} ns, Call<long>(name) {6:F1} ns",
                getString, getKey, getIndex, set, pairs, callRef, callName));
            Check(sink != 0, "the loops ran");
        }
    }
}
