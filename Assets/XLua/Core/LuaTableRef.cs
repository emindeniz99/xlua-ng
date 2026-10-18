/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// LuaScript's tables (docs/design/core-luascript.md, section 2.9; milestone 8):
// LuaTableRef, a handle to a Lua table with raw typed getters and setters;
// LuaKey, a pre-anchored string key; LuaPairs and LuaIPairs, iteration that
// allocates nothing; and the part of LuaScript that keeps every handle (tables,
// functions and buffers) in one slot array. Every operation is one P/Invoke of
// xlua_core_handle (build/xlua_core_sandbox.c). Compiled with the scripting
// define XLUA_LUASCRIPT or XLUA_CORE_ONLY, like LuaScript.
#if XLUA_LUASCRIPT || XLUA_CORE_ONLY
namespace XLua
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Runtime.CompilerServices;
    using System.Text;
    using XLua.LuaDLL;
    using LuaAPI = XLua.LuaDLL.Lua;

    /// <summary>
    /// A handle to a Lua table of a <see cref="LuaScript"/>. Reads and writes
    /// are raw: no metamethod runs, so table access from C# never runs script
    /// code. A struct: creating one allocates nothing; dispose an owning handle
    /// (from <see cref="GetTable(string)"/>, <see cref="LuaScript.NewTable"/>, a
    /// call's result ...) when done, or it keeps its table until the script is
    /// disposed (<see cref="LuaScript.LiveHandles"/> counts them). A copy of a
    /// disposed handle is invalid as well; a second Dispose does nothing.
    /// Borrowed handles (a host function's argument, a value during
    /// <see cref="Pairs"/>) become invalid when their frame or step ends;
    /// <see cref="Retain"/> keeps one. <c>default(LuaTableRef)</c> is no table;
    /// where a method takes an environment, it means <see cref="LuaScript.Globals"/>.
    /// </summary>
    public readonly struct LuaTableRef : IDisposable, IEquatable<LuaTableRef>
    {
        readonly LuaScript script;
        readonly int slot;
        readonly int generation;

        internal LuaTableRef(LuaScript script, int slot, int generation)
        {
            this.script = script;
            this.slot = slot;
            this.generation = generation;
        }

        internal int Slot { get { return slot; } }
        internal int Generation { get { return generation; } }

        /// <summary>The script the table belongs to; null for default(LuaTableRef).</summary>
        public LuaScript Script { get { return script; } }

        /// <summary>False after Dispose, for a copy of a disposed handle, after a borrowed handle's frame or step, after the script is disposed, and for default(LuaTableRef).</summary>
        public bool IsValid { get { return script != null && script.HandleValid(slot, generation, LuaType.Table); } }

        int Native
        {
            get
            {
                if (script == null)
                {
                    throw new ObjectDisposedException("LuaTableRef", "default(LuaTableRef) refers to no table");
                }
                return script.NativeHandle(slot, generation, LuaType.Table);
            }
        }

        // ---- reads (raw) ----
        // The named getters read through one non-generic path (LuaScript.TableFetch and the
        // readers of Call<T>), so that IL2CPP generates no code per type for them.

        /// <summary>
        /// The value of a key as T: long, int, double, float, bool, string,
        /// byte[], <see cref="LuaValue"/>, <see cref="LuaTableRef"/>,
        /// <see cref="LuaFunctionRef"/> or <see cref="LuaBufferRef"/> (as for
        /// <c>Call&lt;T&gt;</c>; tables, functions and buffers are new owning
        /// handles). A missing key (nil) throws <see cref="KeyNotFoundException"/>;
        /// a value of another type <see cref="LuaTypeException"/>.
        /// </summary>
        public T Get<T>(string key) { xlua_value v = script.TableGet(Native, LuaKeyOrValue.Of(key), LuaKind<T>.Code, true); return script.Read<T>(ref v); }
        public T Get<T>(long index) { xlua_value v = script.TableGet(Native, LuaKeyOrValue.Of(index), LuaKind<T>.Code, true); return script.Read<T>(ref v); }
        public T Get<T>(LuaKey key) { xlua_value v = script.TableGet(Native, LuaKeyOrValue.Of(script, key), LuaKind<T>.Code, true); return script.Read<T>(ref v); }

        /// <summary>As <see cref="Get{T}(string)"/>, but a missing key gives <paramref name="fallback"/>.</summary>
        public T Get<T>(string key, T fallback) { xlua_value v = script.TableGet(Native, LuaKeyOrValue.Of(key), LuaKind<T>.Code, false); return v.type == 0 ? fallback : script.Read<T>(ref v); }
        public T Get<T>(LuaKey key, T fallback) { xlua_value v = script.TableGet(Native, LuaKeyOrValue.Of(script, key), LuaKind<T>.Code, false); return v.type == 0 ? fallback : script.Read<T>(ref v); }

        /// <summary>As <see cref="Get{T}(string)"/>, but false for a missing key or a value that does not convert to T.</summary>
        public bool TryGet<T>(string key, out T value)
        {
            xlua_value v = script.TableGet(Native, LuaKeyOrValue.Of(key), LuaKind<T>.Code, false);
            value = default(T);
            if (v.type == 0)
            {
                return false;
            }
            try
            {
                value = script.Read<T>(ref v);
                return true;
            }
            catch (LuaTypeException)
            {
                return false;  // Read released any handle
            }
        }

        /// <summary>The raw value of a key; nil for a missing one (tables and functions are owning handles).</summary>
        public LuaValue Get(LuaValue key) { xlua_value v = Fetch(LuaKeyOrValue.Of(key), true); return script.ResultValue(ref v); }

        xlua_value Fetch(LuaKeyOrValue key, bool handles) { return script.TableFetch(Native, key, handles); }

        static xlua_value Required(xlua_value v, LuaKeyOrValue key)
        {
            if (v.type == 0)
            {
                throw new KeyNotFoundException("the table has no key " + key);
            }
            return v;
        }

        /// <summary>An integer, or a float with an exact integer value; a missing key throws <see cref="KeyNotFoundException"/>.</summary>
        public long GetLong(string key) { var k = LuaKeyOrValue.Of(key); xlua_value v = Required(Fetch(k, false), k); return LuaScript.ReadLong(ref v); }
        public long GetLong(long index) { var k = LuaKeyOrValue.Of(index); xlua_value v = Required(Fetch(k, false), k); return LuaScript.ReadLong(ref v); }
        public long GetLong(LuaKey key) { var k = LuaKeyOrValue.Of(script, key); xlua_value v = Required(Fetch(k, false), k); return LuaScript.ReadLong(ref v); }
        public long GetLong(string key, long fallback) { xlua_value v = Fetch(LuaKeyOrValue.Of(key), false); return v.type == 0 ? fallback : LuaScript.ReadLong(ref v); }
        public bool TryGetLong(string key, out long value) { xlua_value v = Fetch(LuaKeyOrValue.Of(key), false); return LuaScript.TryReadLong(ref v, out value); }
        /// <summary>A number (an integer converts).</summary>
        public double GetDouble(string key) { var k = LuaKeyOrValue.Of(key); xlua_value v = Required(Fetch(k, false), k); return LuaScript.ReadDouble(ref v); }
        public double GetDouble(LuaKey key) { var k = LuaKeyOrValue.Of(script, key); xlua_value v = Required(Fetch(k, false), k); return LuaScript.ReadDouble(ref v); }
        public double GetDouble(string key, double fallback) { xlua_value v = Fetch(LuaKeyOrValue.Of(key), false); return v.type == 0 ? fallback : LuaScript.ReadDouble(ref v); }
        public bool TryGetDouble(string key, out double value)
        {
            xlua_value v = Fetch(LuaKeyOrValue.Of(key), false);
            value = v.type == (int)LuaType.Integer ? v.i : v.n;
            if (v.type == (int)LuaType.Integer || v.type == (int)LuaType.Number) return true;
            value = 0;
            return false;
        }
        /// <summary>Lua's truth of the value; a missing key throws <see cref="KeyNotFoundException"/>.</summary>
        public bool GetBool(string key) { var k = LuaKeyOrValue.Of(key); xlua_value v = Required(Fetch(k, false), k); return LuaScript.ReadBool(ref v); }
        public bool GetBool(string key, bool fallback) { xlua_value v = Fetch(LuaKeyOrValue.Of(key), false); return v.type == 0 ? fallback : LuaScript.ReadBool(ref v); }
        /// <summary>A string value (numbers are not converted); this allocates the string.</summary>
        public string GetString(string key) { var k = LuaKeyOrValue.Of(key); xlua_value v = Required(Fetch(k, false), k); return LuaScript.ReadString(ref v); }
        public string GetString(long index) { var k = LuaKeyOrValue.Of(index); xlua_value v = Required(Fetch(k, false), k); return LuaScript.ReadString(ref v); }
        public string GetString(string key, string fallback) { xlua_value v = Fetch(LuaKeyOrValue.Of(key), false); return v.type == 0 ? fallback : LuaScript.ReadString(ref v); }
        public bool TryGetString(string key, out string value)
        {
            xlua_value v = Fetch(LuaKeyOrValue.Of(key), false);
            value = v.type == (int)LuaType.String ? LuaScript.ReadString(ref v) : null;
            return value != null;
        }
        /// <summary>The bytes of a string value, in a new array.</summary>
        public byte[] GetBytes(string key) { var k = LuaKeyOrValue.Of(key); xlua_value v = Required(Fetch(k, false), k); return LuaScript.ReadBytes(ref v); }

        /// <summary>A table value as a new owning handle; dispose it (<c>using var a = t.GetTable("a");</c>).</summary>
        public LuaTableRef GetTable(string key) { var k = LuaKeyOrValue.Of(key); xlua_value v = Required(Fetch(k, true), k); return script.ReadTableResult(ref v); }
        public LuaTableRef GetTable(long index) { var k = LuaKeyOrValue.Of(index); xlua_value v = Required(Fetch(k, true), k); return script.ReadTableResult(ref v); }
        public bool TryGetTable(string key, out LuaTableRef table)
        {
            xlua_value v = Fetch(LuaKeyOrValue.Of(key), true);
            table = v.type == (int)LuaType.Table ? script.ReadTableResult(ref v) : default(LuaTableRef);
            script.ReleaseValue(ref v);
            return table.Script != null;
        }
        /// <summary>A function value as a new owning handle.</summary>
        public LuaFunctionRef GetFunction(string key) { var k = LuaKeyOrValue.Of(key); xlua_value v = Required(Fetch(k, true), k); return script.ReadFunctionResult(ref v); }

        /// <summary>The type of a key's value (<see cref="LuaType.Nil"/> for a missing key), without reading it.</summary>
        public LuaType TypeOf(string key) { return LuaScript.TypeOfValue(Fetch(LuaKeyOrValue.Of(key), false)); }
        public LuaType TypeOf(long index) { return LuaScript.TypeOfValue(Fetch(LuaKeyOrValue.Of(index), false)); }

        /// <summary>
        /// Copies the bytes of a string value into destination without
        /// allocating: false when the key is missing, the value is not a
        /// string, or it is longer than destination (written is then its length).
        /// </summary>
        public unsafe bool TryCopyBytes(string key, Span<byte> destination, out int written)
        {
            xlua_value v = Fetch(LuaKeyOrValue.Of(key), false);
            written = v.type == (int)LuaType.String ? v.len : 0;
            if (v.type != (int)LuaType.String || v.len > destination.Length)
            {
                return false;
            }
            new ReadOnlySpan<byte>((void*)v.s, v.len).CopyTo(destination);
            return true;
        }

        // ---- writes (raw) ----

        /// <summary>Sets a key raw (no __newindex). A nil value removes the key; a nil or NaN key throws <see cref="LuaRuntimeException"/>. May throw <see cref="LuaMemoryLimitException"/>.</summary>
        public void Set(string key, LuaValue value) { script.TableWrite(Native, LuaKeyOrValue.Of(key), value); }
        public void Set(long index, LuaValue value) { script.TableWrite(Native, LuaKeyOrValue.Of(index), value); }
        public void Set(LuaKey key, LuaValue value) { script.TableWrite(Native, LuaKeyOrValue.Of(script, key), value); }
        public void Set(LuaValue key, LuaValue value) { script.TableWrite(Native, LuaKeyOrValue.Of(key), value); }

        /// <summary>Sets <paramref name="key"/> to a new table and returns an owning handle to it.</summary>
        public LuaTableRef SetNewTable(string key, int arrayCapacity = 0, int hashCapacity = 0)
        {
            int table = Native;
            LuaTableRef t = script.NewTable(arrayCapacity, hashCapacity);
            try
            {
                script.TableWrite(table, LuaKeyOrValue.Of(key), t);
            }
            catch
            {
                t.Dispose();
                throw;
            }
            return t;
        }

        /// <summary>The raw length (<c>rawlen</c>): a border of the array part.</summary>
        public long Length { get { return script.TableLength(Native); } }

        // ---- iteration ----

        /// <summary>
        /// The pairs of the table in Lua's <c>next</c> order, for <c>foreach</c>:
        /// allocates nothing (the key and value stay on the native side; only
        /// decoding a string allocates). Table, function and buffer keys and
        /// values are borrowed until the next step. Assigning existing keys
        /// during the traversal is fine; adding keys is not (Lua's rule).
        /// </summary>
        public LuaPairs Pairs() { _ = Native; return new LuaPairs(script, slot, generation, false); }

        /// <summary>The values at 1, 2, ... up to the first nil, as <see cref="Pairs"/> does.</summary>
        public LuaPairs IPairs() { _ = Native; return new LuaPairs(script, slot, generation, true); }

        // ---- handles ----

        /// <summary>A new owning handle to the same table (also from a borrowed one); dispose it separately.</summary>
        public LuaTableRef Retain()
        {
            return script.RetainTable(Native);
        }

        /// <summary>Releases an owning handle; a borrowed one, <see cref="LuaScript.Globals"/>, a second Dispose or one after the script's do nothing.</summary>
        public void Dispose()
        {
            if (script != null)
            {
                script.ReleaseHandle(slot, generation, LuaType.Table);
            }
        }

        /// <summary>
        /// The table as a tree of <c>Dictionary&lt;string, object&gt;</c>, with
        /// <c>List&lt;object&gt;</c> for sequences (keys 1 to n) below the top,
        /// boxed numbers (long, double), bool, string and byte[] (buffers).
        /// Number keys become their invariant text; functions and other values
        /// become their type name. A debugging and migration helper: it
        /// allocates. A cycle, a table nested deeper than maxDepth, or a key
        /// that is a table or function throws <see cref="InvalidOperationException"/>.
        /// </summary>
        public Dictionary<string, object> ToDictionary(int maxDepth = 16)
        {
            int table = Native;
            if (maxDepth < 1)
            {
                throw new ArgumentOutOfRangeException("maxDepth", maxDepth, "maxDepth must be 1 or more");
            }
            var path = new int[maxDepth];  // the native handles of the tables being converted, outermost first
            path[0] = table;
            return (Dictionary<string, object>)script.TableToObject(this, path, 1, false);
        }

        // ---- calls of the functions this table holds (an environment) ----

        /// <summary>
        /// Calls the function <paramref name="function"/> of this table as an
        /// environment: raw lookups, dotted names walk tables, and a first name
        /// missing here is looked up in the frozen base (as for <see cref="LuaScript.Call(string)"/>).
        /// The span forms only, to keep the code small: reuse an array for the arguments,
        /// or call a <see cref="LuaFunctionRef"/> from <see cref="GetFunction"/>.
        /// </summary>
        public void Call(string function) { script.CallIn(Native, function, new LuaScript.Args(), default(Span<LuaValue>)); }
        public void Call(string function, ReadOnlySpan<LuaValue> args) { script.CallIn(Native, function, new LuaScript.Args(args), default(Span<LuaValue>)); }
        public int Call(string function, ReadOnlySpan<LuaValue> args, Span<LuaValue> results) { return script.CallIn(Native, function, new LuaScript.Args(args), results); }
        public T Call<T>(string function) { xlua_value r = script.One(LuaScript.XLUA_CALL_GLOBAL, Native, 0, function, null, new LuaScript.Args()); return script.Read<T>(ref r); }
        public T Call<T>(string function, ReadOnlySpan<LuaValue> args) { xlua_value r = script.One(LuaScript.XLUA_CALL_GLOBAL, Native, 0, function, null, new LuaScript.Args(args)); return script.Read<T>(ref r); }

        // ---- identity ----

        /// <summary>The same table (two handles may refer to one table); invalid handles are equal only to copies of themselves.</summary>
        public bool Equals(LuaTableRef other)
        {
            return LuaScript.SameValue(script, slot, generation, other.script, other.slot, other.generation, LuaType.Table);
        }

        public override bool Equals(object obj)
        {
            return obj is LuaTableRef && Equals((LuaTableRef)obj);
        }

        public override int GetHashCode()
        {
            return script == null ? 0 : RuntimeHelpers.GetHashCode(script);
        }

        public static bool operator ==(LuaTableRef a, LuaTableRef b) { return a.Equals(b); }
        public static bool operator !=(LuaTableRef a, LuaTableRef b) { return !a.Equals(b); }

        public override string ToString()
        {
            return IsValid ? "table" : "table (invalid handle)";
        }
    }

    /// <summary>
    /// A string key anchored in its script for the script's lifetime
    /// (<see cref="LuaScript.Key"/>): reads and writes through it skip the
    /// UTF-8 encoding of the name, the fast path for hot fields. Not a handle:
    /// nothing to dispose, and <see cref="LuaScript.LiveHandles"/> does not count it.
    /// </summary>
    public readonly struct LuaKey : IEquatable<LuaKey>
    {
        readonly LuaScript script;
        readonly int handle;
        readonly string name;

        internal LuaKey(LuaScript script, int handle, string name)
        {
            this.script = script;
            this.handle = handle;
            this.name = name;
        }

        internal int Handle { get { return handle; } }

        /// <summary>The script the key belongs to; null for default(LuaKey).</summary>
        public LuaScript Script { get { return script; } }

        /// <summary>The key's text.</summary>
        public string Name { get { return name; } }

        /// <summary>False for default(LuaKey) and after the script is disposed.</summary>
        public bool IsValid { get { return script != null && !script.IsDisposed; } }

        public bool Equals(LuaKey other) { return script == other.script && handle == other.handle; }
        public override bool Equals(object obj) { return obj is LuaKey && Equals((LuaKey)obj); }
        public override int GetHashCode() { return handle; }
        public override string ToString() { return name ?? "(no key)"; }
    }

    // A key (or a value) as one of the forms the table methods take: a LuaValue,
    // or a LuaKey's anchored string.
    internal readonly struct LuaKeyOrValue
    {
        internal readonly LuaValue Value;
        internal readonly int KeyHandle;  // a LuaKey's native handle, else 0

        LuaKeyOrValue(LuaValue value, int keyHandle)
        {
            Value = value;
            KeyHandle = keyHandle;
        }

        internal static LuaKeyOrValue Of(string key)
        {
            if (key == null)
            {
                throw new ArgumentNullException("key");
            }
            return new LuaKeyOrValue(key, 0);
        }

        internal static LuaKeyOrValue Of(long index) { return new LuaKeyOrValue(index, 0); }
        internal static LuaKeyOrValue Of(LuaValue key) { return new LuaKeyOrValue(key, 0); }

        internal static LuaKeyOrValue Of(LuaScript table, LuaKey key)
        {
            if (key.Script == null || key.Script != table)
            {
                throw new ArgumentException(key.Script == null ? "default(LuaKey) is no key" : "the LuaKey belongs to another LuaScript", "key");
            }
            return new LuaKeyOrValue(default(LuaValue), key.Handle);
        }

        public override string ToString()
        {
            return KeyHandle != 0 ? "(LuaKey)" : Value.ToString();
        }
    }

    /// <summary>
    /// The enumerator of <see cref="LuaTableRef.Pairs"/> and <see cref="LuaTableRef.IPairs"/>:
    /// a struct, so <c>foreach</c> allocates nothing. Its step lives in the script
    /// (an iterator, counted by <see cref="LuaScript.LiveHandles"/> until the end or Dispose).
    /// </summary>
    public struct LuaPairs : IDisposable
    {
        readonly LuaScript script;
        readonly int slot;      // the table's handle, checked at every step
        readonly int generation;
        readonly bool ipairs;
        int iter;               // the script's iterator, + 1; 0 before the first step
        int step;
        xlua_value key, value;

        internal LuaPairs(LuaScript script, int slot, int generation, bool ipairs)
        {
            this.script = script;
            this.slot = slot;
            this.generation = generation;
            this.ipairs = ipairs;
            iter = 0;
            step = 0;
            key = default(xlua_value);
            value = default(xlua_value);
        }

        public LuaPairs GetEnumerator() { return this; }

        /// <summary>The next pair; false at the end (the iterator is then released).</summary>
        public unsafe bool MoveNext()
        {
            if (script == null)
            {
                return false;
            }
            if (iter < 0)
            {
                return false;  // ended or disposed
            }
            fixed (xlua_value* k = &key)
            fixed (xlua_value* v = &value)
            {
                bool more = script.IterStep(script.NativeHandle(slot, generation, LuaType.Table), ipairs, ref iter, k, v);
                if (more)
                {
                    step = script.IterSerial(iter);
                }
                return more;
            }
        }

        /// <summary>The current pair; its strings and borrowed handles are valid until the next step.</summary>
        public LuaPair Current { get { return new LuaPair(script, iter, step, key, value); } }

        /// <summary>Releases the iterator (foreach does it); a second Dispose does nothing.</summary>
        public void Dispose()
        {
            if (script != null && iter > 0)
            {
                script.IterRelease(iter);
            }
            iter = -1;
        }
    }

    /// <summary>
    /// One pair of <see cref="LuaPairs"/>. <see cref="Key"/> and <see cref="Value"/>
    /// decode strings (which allocates); <see cref="KeyType"/>, <see cref="KeyEquals"/>,
    /// <see cref="KeyUtf8"/> and the numbers do not. Valid until the enumerator's next step.
    /// </summary>
    public readonly struct LuaPair
    {
        readonly LuaScript script;
        readonly int iter;
        readonly int step;
        readonly xlua_value key, value;

        internal LuaPair(LuaScript script, int iter, int step, xlua_value key, xlua_value value)
        {
            this.script = script;
            this.iter = iter;
            this.step = step;
            this.key = key;
            this.value = value;
        }

        void Check()
        {
            if (script == null || !script.IterCurrent(iter, step))
            {
                throw new InvalidOperationException("this LuaPair is from an earlier step of its iteration, or the iteration ended");
            }
        }

        public LuaType KeyType { get { Check(); return LuaScript.TypeOfValue(key); } }
        public LuaType ValueType { get { Check(); return LuaScript.TypeOfValue(value); } }

        /// <summary>The key; a table or function key is a borrowed handle.</summary>
        public LuaValue Key { get { Check(); return script.IterValue(iter, key, false); } }

        /// <summary>The value; a table, function or buffer is a borrowed handle (<see cref="LuaTableRef.Retain"/> keeps it).</summary>
        public LuaValue Value { get { Check(); return script.IterValue(iter, value, true); } }

        /// <summary>Whether the key is a string equal to the UTF-8 of text, compared without decoding.</summary>
        public unsafe bool KeyEquals(string text)
        {
            Check();
            if (text == null)
            {
                throw new ArgumentNullException("text");
            }
            return key.type == (int)LuaType.String && LuaCall.TextEquals((byte*)key.s, key.len, text);
        }

        /// <summary>The bytes of a string key without a copy; valid until the next step.</summary>
        public unsafe ReadOnlySpan<byte> KeyUtf8
        {
            get
            {
                Check();
                if (key.type != (int)LuaType.String)
                {
                    throw LuaValue.TypeError(LuaType.String, LuaScript.TypeOfValue(key), 0);
                }
                return new ReadOnlySpan<byte>((void*)key.s, key.len);
            }
        }

        /// <summary>The bytes of a string value without a copy; valid until the next step.</summary>
        public unsafe ReadOnlySpan<byte> ValueUtf8
        {
            get
            {
                Check();
                if (value.type != (int)LuaType.String)
                {
                    throw LuaValue.TypeError(LuaType.String, LuaScript.TypeOfValue(value), 0);
                }
                return new ReadOnlySpan<byte>((void*)value.s, value.len);
            }
        }

        public void Deconstruct(out LuaValue key, out LuaValue value)
        {
            key = Key;
            value = Value;
        }

        public override string ToString()
        {
            return script != null && script.IterCurrent(iter, step) ? "[" + Key + "] = " + Value : "(stale pair)";
        }
    }

    public sealed unsafe partial class LuaScript
    {
        // xlua_core_handle's operations and the handle types of xlua_value (build/xlua_core.h).
        const int XLUA_HANDLE_GET = 0, XLUA_HANDLE_SET = 1, XLUA_HANDLE_LEN = 2, XLUA_HANDLE_NEXT = 3, XLUA_HANDLE_INEXT = 4;
        const int XLUA_HANDLE_NEWTABLE = 5, XLUA_HANDLE_NEWENV = 6, XLUA_HANDLE_REF = 7, XLUA_HANDLE_KEY = 8, XLUA_HANDLE_EQUAL = 9;
        const int XLUA_HANDLE_NEWBUFFER = 10, XLUA_HANDLE_BUFFER = 11;
        const int XLUA_T_TABLE = 5, XLUA_T_FUNC = 6, XLUA_T_THREAD = 9;
        const byte SlotFree = 0, SlotOwned = 1, SlotBorrowed = 2, SlotPermanent = 3;

        // One slot per handle of any kind. A handle is (slot, generation); the
        // generation changes when a slot is handed out again, so stale copies
        // fail. Slot 0 is Globals (native handle 0, the default environment).
        // A class, made once per slot index and reused: an array of a struct costs an
        // IL2CPP player the generic array and comparer code of that struct.
        sealed class HandleSlot
        {
            public int nativeRef;   // the native handle: a registry reference, or an iterator's (borrowed)
            public int generation;
            public IntPtr data;     // buffers: the bytes, which never move while the handle lives
            public int length;      // buffers: the length; coroutines: the LuaCoroutineStatus the handle came with
            public LuaType kind;    // Table, Function, Buffer or Coroutine
            public byte state;      // SlotFree, SlotOwned, SlotBorrowed (released by its frame or step), SlotPermanent
            public LuaCoroutine owner;  // coroutines: the object of the handle (LuaValue.AsCoroutine), made on first use
        }

        HandleSlot[] slots = GlobalsSlot();
        int usedSlots = 1;  // slots ever handed out, Globals included
        int[] freeSlots = new int[0];
        int freeSlotCount;
        int liveHandles;
        int globalsRef;     // a registry reference of the default environment, for Register(Globals, ...)
        Dictionary<string, int> keys;  // LuaScript.Key: name -> native handle

        static HandleSlot[] GlobalsSlot()
        {
            var s = new HandleSlot[8];
            s[0] = new HandleSlot();
            s[0].generation = 1;
            s[0].kind = LuaType.Table;
            s[0].state = SlotPermanent;
            return s;
        }

        /// <summary>Handles not yet disposed (tables, functions, buffers, coroutines, unfinished iterations), for leak checks; borrowed handles, Globals and keys do not count.</summary>
        public int LiveHandles { get { CheckOpen(); return liveHandles; } }

        /// <summary>The default environment: the globals of <see cref="Load(string, string)"/> and <see cref="Call(string)"/>. Never needs disposing.</summary>
        public LuaTableRef Globals { get { CheckOpen(); return new LuaTableRef(this, 0, 1); } }

        /// <summary>A new table; dispose the handle.</summary>
        public LuaTableRef NewTable(int arrayCapacity = 0, int hashCapacity = 0)
        {
            if (arrayCapacity < 0 || hashCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(arrayCapacity < 0 ? "arrayCapacity" : "hashCapacity", "capacities must not be negative");
            }
            xlua_value* v = stackalloc xlua_value[4];
            v[1].i = arrayCapacity;
            v[1].len = hashCapacity;
            Op(XLUA_HANDLE_NEWTABLE, v);
            int slot = AddHandle(LuaType.Table, (int)v[0].i, SlotOwned);
            return new LuaTableRef(this, slot, slots[slot].generation);
        }

        /// <summary>
        /// A fresh environment over the frozen base (section 4 of the design):
        /// its own globals, <c>_G</c> and copies of the libraries, for
        /// <see cref="Load(string, string, LuaTableRef)"/> and <see cref="Call(string)"/>
        /// through <see cref="LuaTableRef.Call(string)"/>. Dispose the handle.
        /// </summary>
        public LuaTableRef NewEnvironment()
        {
            xlua_value* v = stackalloc xlua_value[4];
            Op(XLUA_HANDLE_NEWENV, v);
            int slot = AddHandle(LuaType.Table, (int)v[0].i, SlotOwned);
            return new LuaTableRef(this, slot, slots[slot].generation);
        }

        /// <summary>A key anchored for the script's lifetime (the same LuaKey for the same name).</summary>
        public LuaKey Key(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException("name");
            }
            IntPtr state = Owned;
            int handle;
            if (keys != null && keys.TryGetValue(name, out handle))
            {
                return new LuaKey(this, handle, name);
            }
            int n = Encoding.UTF8.GetByteCount(name);
            if (scratch.Length < n)
            {
                scratch = new byte[Math.Max(n, scratch.Length * 2)];
            }
            xlua_value* v = stackalloc xlua_value[4];
            int status;
            fixed (byte* s = scratch)
            {
                v[1].type = (int)LuaType.String;
                v[1].len = Encoding.UTF8.GetBytes(name, 0, name.Length, scratch, 0);
                v[1].s = (IntPtr)s;
                status = LuaAPI.xlua_core_handle(state, XLUA_HANDLE_KEY, v);
            }
            if (status != XLUA_OK)
            {
                throw OpError(status, v);
            }
            handle = (int)v[2].i;
            if (keys == null)
            {
                keys = new Dictionary<string, int>();
            }
            keys[name] = handle;
            return new LuaKey(this, handle, name);
        }

        // ---- slots ----

        int AddHandle(LuaType kind, int nativeRef, byte state, int length = -1)
        {
            int slot;
            if (freeSlotCount > 0)
            {
                slot = freeSlots[--freeSlotCount];
            }
            else
            {
                if (usedSlots == slots.Length)
                {
                    Array.Resize(ref slots, slots.Length * 2);
                }
                slot = usedSlots++;
                if (slots[slot] == null)
                {
                    slots[slot] = new HandleSlot();
                }
            }
            int generation = slots[slot].generation + 1;
            if (generation <= 0)
            {
                generation = 1;  // wrapped after 2^31 handles in one slot
            }
            slots[slot].nativeRef = nativeRef;
            slots[slot].generation = generation;
            slots[slot].data = IntPtr.Zero;
            slots[slot].length = length;
            slots[slot].kind = kind;
            slots[slot].state = state;
            if (state == SlotOwned)
            {
                liveHandles++;
            }
            return slot;
        }

        void FreeSlot(int slot)
        {
            if (slots[slot].state == SlotOwned)
            {
                liveHandles--;
            }
            slots[slot].state = SlotFree;
            slots[slot].nativeRef = 0;
            slots[slot].data = IntPtr.Zero;
            LuaCoroutine co = slots[slot].owner;
            if (co != null)
            {
                slots[slot].owner = null;
                co.ReleaseValues();  // what it yielded or returned was borrowed from it
            }
            if (freeSlots.Length == freeSlotCount)
            {
                Array.Resize(ref freeSlots, Math.Max(8, freeSlotCount * 2));
            }
            freeSlots[freeSlotCount++] = slot;
        }

        internal bool HandleValid(int slot, int generation, LuaType kind)
        {
            return L != IntPtr.Zero && (uint)slot < (uint)usedSlots && generation != 0 && slots[slot].generation == generation
                && slots[slot].state != SlotFree && slots[slot].kind == kind;
        }

        // The native handle of a valid handle of this script, on the owner thread.
        internal int NativeHandle(int slot, int generation, LuaType kind)
        {
            if (!HandleValid(slot, generation, kind))
            {
                throw new ObjectDisposedException(kind == LuaType.Table ? "LuaTableRef" : kind == LuaType.Function ? "LuaFunctionRef"
                    : kind == LuaType.Coroutine ? "LuaCoroutine" : "LuaBufferRef",
                    L == IntPtr.Zero ? "the LuaScript is disposed" : "the handle is disposed, or a borrowed handle outlived its frame or step");
            }
            CheckThread();
            return slots[slot].nativeRef;
        }

        internal void ReleaseHandle(int slot, int generation, LuaType kind)
        {
            if (!HandleValid(slot, generation, kind) || slots[slot].state != SlotOwned)
            {
                return;
            }
            LuaAPI.xlua_core_unref(Owned, slots[slot].nativeRef);
            FreeSlot(slot);
        }

        // A handle value of a LuaValue (table, function or buffer) of this script: its native handle.
        // what: the argument (0-based), or WhatKey or WhatValue, for the message.
        const int WhatKey = -1, WhatValue = -2, WhatResult = -3;

        int ValueHandle(LuaValue v, int what)
        {
            var owner = v.Object as LuaScript;
            if (owner == null || owner != this)
            {
                throw new ArgumentException(What(what) + " is a " + LuaValue.Name(v.Type)
                    + (owner == null ? " without a handle" : " of another LuaScript"), what >= 0 ? "args" : "value");
            }
            return NativeHandle((int)v.Bits, (int)(v.Bits >> 32), v.Type);
        }

        static string What(int what)
        {
            return what >= 0 ? "argument " + (what + 1) : what == WhatKey ? "the key" : what == WhatValue ? "the value" : "a result";
        }

        internal static bool SameValue(LuaScript a, int slotA, int genA, LuaScript b, int slotB, int genB, LuaType kind)
        {
            if (a != b)
            {
                return false;
            }
            if (slotA == slotB && genA == genB)
            {
                return true;
            }
            if (a == null || !a.HandleValid(slotA, genA, kind) || !a.HandleValid(slotB, genB, kind))
            {
                return false;
            }
            xlua_value* v = stackalloc xlua_value[4];
            v[0].i = a.slots[slotA].nativeRef;
            v[1].i = a.slots[slotB].nativeRef;
            a.Op(XLUA_HANDLE_EQUAL, v);
            return v[2].i != 0;
        }

        // ---- the native operation ----

        void Op(int op, xlua_value* v)
        {
            int status = LuaAPI.xlua_core_handle(Owned, op, v);
            if (status != XLUA_OK)
            {
                throw OpError(status, v);
            }
        }

        Exception OpError(int status, xlua_value* v)
        {
            switch (status)
            {
                case XLUA_ERRMEM:
                    {
                        xlua_core_stats stats = Stats();
                        return new LuaMemoryLimitException("not enough memory", stats.memory_limit, stats.memory_used, false);
                    }
                case XLUA_ERRRUN:
                    return new LuaRuntimeException(v[2].type == (int)LuaType.String ? ReadString(ref v[2]) : "error in a table operation", null);
                case XLUA_ERRTYPE:
                    return new ArgumentException("this value cannot be passed to Lua");
                case XLUA_ERRHANDLE:
                    return new InvalidOperationException("not a valid handle of this LuaScript");
                default:
                    return new NotSupportedException("this xlua native library cannot do this (status " + status + ")");
            }
        }

        // A value from C# into a native record: strings into the pinned scratch at offset.
        void PutValue(LuaValue v, xlua_value* a, byte* s, ref int offset, int what)
        {
            a->type = (int)v.Type;
            a->len = 0;
            a->i = v.Bits;
            switch (v.Type)
            {
                case LuaType.Nil:
                case LuaType.Boolean:
                case LuaType.Integer:
                case LuaType.Number:
                    return;
                case LuaType.String:
                    {
                        var bytes = v.Object as byte[];
                        int len;
                        if (bytes != null)
                        {
                            len = bytes.Length;
                            if (len > 0)
                            {
                                fixed (byte* b = bytes)
                                {
                                    Buffer.MemoryCopy(b, s + offset, len, len);
                                }
                            }
                        }
                        else
                        {
                            string str = (string)v.Object;
                            fixed (char* c = str)
                            {
                                len = Encoding.UTF8.GetBytes(c, str.Length, s + offset, scratch.Length - offset);
                            }
                        }
                        a->s = (IntPtr)(s + offset);
                        a->len = len;
                        offset += len;
                        return;
                    }
                case LuaType.Table:
                case LuaType.Function:
                case LuaType.Buffer:
                case LuaType.Coroutine:
                    a->i = ValueHandle(v, what);
                    if (v.Type == LuaType.Buffer)
                    {
                        a->len = 0;
                    }
                    return;
                default:
                    throw new ArgumentException(What(what) + " is a " + LuaValue.Name(v.Type) + " without a handle, which cannot be passed", what >= 0 ? "args" : "value");
            }
        }

        // One table operation with a key (and for SET a value): strings are encoded into the scratch array, pinned for the call.
        void TableOp(int op, int table, LuaKeyOrValue key, LuaValue value, int borrow, xlua_value* v)
        {
            IntPtr state = Owned;
            int total = Utf8Length(key.Value) + (op == XLUA_HANDLE_SET ? Utf8Length(value) : 0);
            if (scratch.Length < total)
            {
                scratch = new byte[Math.Max(total, scratch.Length * 2)];
            }
            int status;
            fixed (byte* s = scratch)
            {
                int offset = 0;
                v[0].type = XLUA_T_TABLE;
                v[0].len = 0;
                v[0].i = table;
                if (key.KeyHandle != 0)
                {
                    v[1].type = (int)LuaType.String;
                    v[1].len = -1;
                    v[1].i = key.KeyHandle;
                }
                else
                {
                    PutValue(key.Value, v + 1, s, ref offset, WhatKey);
                }
                if (op == XLUA_HANDLE_SET)
                {
                    PutValue(value, v + 2, s, ref offset, WhatValue);
                }
                v[3].type = 0;
                v[3].len = 0;
                v[3].i = borrow;
                status = LuaAPI.xlua_core_handle(state, op, v);
            }
            if (status != XLUA_OK)
            {
                throw OpError(status, v);
            }
        }

        // The raw value of a key: with handles, a table, function or buffer value is a new
        // owning handle; without, its type only (no native handle is made).
        internal xlua_value TableFetch(int table, LuaKeyOrValue key, bool handles)
        {
            xlua_value* v = stackalloc xlua_value[4];
            TableOp(XLUA_HANDLE_GET, table, key, default(LuaValue), handles ? 0 : int.MinValue, v);
            return v[2];
        }

        // The raw value of a key for Get<T> of a result kind (LuaKinds): with handles for the
        // kinds that keep them; a missing key throws when it is required.
        internal xlua_value TableGet(int table, LuaKeyOrValue key, int kind, bool required)
        {
            xlua_value v = TableFetch(table, key, LuaKinds.KeepsHandles(kind));
            if (v.type == 0 && required)
            {
                throw new KeyNotFoundException("the table has no key " + key);
            }
            return v;
        }

        internal void ReleaseValue(ref xlua_value v)
        {
            ReleaseResult(ref v);
        }

        internal static bool TryReadLong(ref xlua_value v, out long value)
        {
            if (v.type == (int)LuaType.Integer)
            {
                value = v.i;
                return true;
            }
            if (v.type == (int)LuaType.Number)
            {
                return FloatToLong(v.n, out value);
            }
            value = 0;
            return false;
        }

        internal void TableWrite(int table, LuaKeyOrValue key, LuaValue value)
        {
            xlua_value* v = stackalloc xlua_value[4];
            TableOp(XLUA_HANDLE_SET, table, key, value, 0, v);
        }

        internal long TableLength(int table)
        {
            xlua_value* v = stackalloc xlua_value[4];
            v[0].i = table;
            Op(XLUA_HANDLE_LEN, v);
            return v[2].i;
        }

        // A new owning handle of the value of a native handle (REF), of the kind the result has.
        int RetainNative(int native, LuaType kind)
        {
            xlua_value* v = stackalloc xlua_value[4];
            v[0].len = 0;
            v[0].i = native;
            Op(XLUA_HANDLE_REF, v);
            return AddHandle(kind, (int)v[2].i, SlotOwned);
        }

        internal LuaTableRef RetainTable(int native)
        {
            int slot = RetainNative(native, LuaType.Table);
            return new LuaTableRef(this, slot, slots[slot].generation);
        }

        internal LuaFunctionRef RetainFunction(int native)
        {
            int slot = RetainNative(native, LuaType.Function);
            return new LuaFunctionRef(this, slot, slots[slot].generation);
        }

        // ---- results that hold handles ----

        static bool IsHandle(int type)
        {
            return type == XLUA_T_TABLE || type == XLUA_T_FUNC || type == XLUA_T_BUFFER || type == XLUA_T_THREAD;
        }

        internal static LuaType TypeOfValue(xlua_value v)
        {
            return v.type >= 0 && v.type <= (int)LuaType.Coroutine ? (LuaType)v.type : LuaType.Other;
        }

        // A native handle of a result (table, function, buffer or coroutine): a slot of this script.
        int Adopt(ref xlua_value v, byte state = SlotOwned)
        {
            int native = (int)v.i;
            LuaType kind = (LuaType)v.type;
            int length = kind == LuaType.Coroutine ? v.len : -1;  // its status
            v.type = (int)LuaType.Other;  // adopted: not released again
            v.i = 0;
            return AddHandle(kind, native, state, length);
        }

        // A result read as something else than a handle: release its native handle.
        void ReleaseResult(ref xlua_value v)
        {
            if (IsHandle(v.type))
            {
                if (L != IntPtr.Zero && v.i > 0)
                {
                    LuaAPI.xlua_core_unref(L, (int)v.i);
                }
                v.type = (int)LuaType.Other;
                v.i = 0;
            }
        }

        // A result as a LuaValue: tables, functions and buffers become owning handles the caller disposes.
        internal LuaValue ResultValue(ref xlua_value v)
        {
            if (IsHandle(v.type))
            {
                LuaType kind = (LuaType)v.type;
                int slot = Adopt(ref v);
                return LuaValue.Handle(kind, this, slot, slots[slot].generation);
            }
            return ToValue(ref v);
        }

        internal LuaTableRef ReadTableResult(ref xlua_value v)
        {
            if (v.type == XLUA_T_TABLE)
            {
                int slot = Adopt(ref v);
                return new LuaTableRef(this, slot, slots[slot].generation);
            }
            CheckNil(ref v, LuaType.Table);
            return default(LuaTableRef);
        }

        internal LuaCoroutine ReadCoroutineResult(ref xlua_value v)
        {
            if (v.type == XLUA_T_THREAD)
            {
                int slot = Adopt(ref v);
                return CoroutineOf(slot, slots[slot].generation);
            }
            CheckNil(ref v, LuaType.Coroutine);
            return null;
        }

        // The LuaCoroutine of a coroutine handle: one object per handle, made on first use.
        internal LuaCoroutine CoroutineOf(int slot, int generation)
        {
            NativeHandle(slot, generation, LuaType.Coroutine);
            LuaCoroutine co = slots[slot].owner;
            if (co == null)
            {
                int status = slots[slot].length;
                co = new LuaCoroutine(this, slot, generation, status >= 0 ? (LuaCoroutineStatus)status : LuaCoroutineStatus.Yielded, false);
                slots[slot].owner = co;
            }
            return co;
        }

        internal LuaFunctionRef ReadFunctionResult(ref xlua_value v)
        {
            if (v.type == XLUA_T_FUNC)
            {
                int slot = Adopt(ref v);
                return new LuaFunctionRef(this, slot, slots[slot].generation);
            }
            CheckNil(ref v, LuaType.Function);
            return default(LuaFunctionRef);
        }

        // A result of the wrong type for a handle reader: nil reads as no handle; any other type
        // releases its handle and throws.
        internal void CheckNil(ref xlua_value v, LuaType expected)
        {
            if (v.type == 0)
            {
                return;
            }
            LuaType actual = TypeOfValue(v);
            ReleaseResult(ref v);
            throw LuaValue.TypeError(expected, actual, 0);
        }

        // Disposes the handles a result holds, on the error path of the tuple reads.
        static void DisposeResult(TypedReference value)
        {
            Type t = __reftype(value);
            if (t == typeof(LuaBufferRef))
            {
                LuaBufferRef b = __refvalue(value, LuaBufferRef);  // a local: IL2CPP mistranslates a call on the reference
                b.Dispose();
            }
            else if (t == typeof(LuaTableRef))
            {
                LuaTableRef table = __refvalue(value, LuaTableRef);  // a local: IL2CPP mistranslates a call on the reference
                table.Dispose();
            }
            else if (t == typeof(LuaFunctionRef))
            {
                LuaFunctionRef f = __refvalue(value, LuaFunctionRef);  // a local: IL2CPP mistranslates a call on the reference
                f.Dispose();
            }
            else if (t == typeof(LuaValue))
            {
                LuaValue v = __refvalue(value, LuaValue);  // a local: IL2CPP mistranslates a call on the reference
                v.Dispose();
            }
            else if (t == typeof(LuaCoroutine))
            {
                LuaCoroutine co = __refvalue(value, LuaCoroutine);
                if (co != null)
                {
                    co.Dispose();
                }
            }
        }

        // ---- iteration ----

        // One per unfinished iteration; the native iterator (a two-slot table)
        // is kept when an iteration ends and reused by the next one.
        sealed class IterState
        {
            public int nativeRef;   // 0 until the first step
            public int serial;      // changes with every step, so old pairs fail
            public int keySlot;     // borrowed handles of the current key and value, or -1
            public int valueSlot;
            public bool active;
        }

        IterState[] iters = new IterState[0];
        int iterCount;

        // A step of iteration iter (+ 1; 0 starts a new one). Fills key and value; false at the end, which releases the iterator.
        internal bool IterStep(int table, bool ipairs, ref int iter, xlua_value* key, xlua_value* value)
        {
            IntPtr state = Owned;
            bool start = iter == 0;
            if (start)
            {
                iter = IterAcquire() + 1;
            }
            int i = iter - 1;
            IterInvalidate(i);
            xlua_value* v = stackalloc xlua_value[4];
            v[0].type = XLUA_T_TABLE;
            v[0].i = table;
            v[3].i = iters[i].nativeRef;
            v[3].len = start ? 1 : 0;
            int status = LuaAPI.xlua_core_handle(state, ipairs ? XLUA_HANDLE_INEXT : XLUA_HANDLE_NEXT, v);
            if (status != XLUA_OK)
            {
                throw OpError(status, v);
            }
            iters[i].nativeRef = (int)v[3].i;
            if (v[1].type == 0)
            {
                IterRelease(iter);
                iter = -1;
                return false;
            }
            iters[i].serial++;
            *key = v[1];
            *value = v[2];
            return true;
        }

        int IterAcquire()
        {
            for (int i = 0; i < iterCount; i++)
            {
                if (!iters[i].active)
                {
                    iters[i].active = true;
                    liveHandles++;
                    return i;
                }
            }
            if (iterCount == iters.Length)
            {
                Array.Resize(ref iters, Math.Max(4, iterCount * 2));
            }
            if (iters[iterCount] == null)
            {
                iters[iterCount] = new IterState();
            }
            iters[iterCount].keySlot = -1;
            iters[iterCount].valueSlot = -1;
            iters[iterCount].active = true;
            liveHandles++;
            return iterCount++;
        }

        // The borrowed handles of the last step end.
        void IterInvalidate(int i)
        {
            if (iters[i].keySlot >= 0)
            {
                FreeSlot(iters[i].keySlot);
                iters[i].keySlot = -1;
            }
            if (iters[i].valueSlot >= 0)
            {
                FreeSlot(iters[i].valueSlot);
                iters[i].valueSlot = -1;
            }
        }

        internal void IterRelease(int iter)
        {
            int i = iter - 1;
            if (L == IntPtr.Zero || i < 0 || i >= iterCount || !iters[i].active)
            {
                return;
            }
            IterInvalidate(i);
            iters[i].serial++;
            iters[i].active = false;
            liveHandles--;
        }

        internal int IterSerial(int iter)
        {
            return iters[iter - 1].serial;
        }

        internal bool IterCurrent(int iter, int serial)
        {
            return L != IntPtr.Zero && iter > 0 && iter <= iterCount && iters[iter - 1].active && iters[iter - 1].serial == serial;
        }

        // The key or value of the current step as a LuaValue; a handle type gets a borrowed slot of the iterator.
        internal LuaValue IterValue(int iter, xlua_value v, bool isValue)
        {
            if (!IsHandle(v.type))
            {
                return ToValue(ref v);
            }
            int i = iter - 1;
            int slot = isValue ? iters[i].valueSlot : iters[i].keySlot;
            if (slot < 0)
            {
                slot = AddHandle((LuaType)v.type, (int)v.i, SlotBorrowed, v.type == XLUA_T_THREAD ? v.len : -1);
                if (isValue)
                {
                    iters[i].valueSlot = slot;
                }
                else
                {
                    iters[i].keySlot = slot;
                }
            }
            return LuaValue.Handle((LuaType)v.type, this, slot, slots[slot].generation);
        }

        // ---- ToDictionary ----

        // The keys and values go into object arrays, not lists of structs: value-type
        // instantiations of List<T> cost an IL2CPP player about 8 KB of code each.
        internal object TableToObject(LuaTableRef t, int[] path, int depth, bool allowList)
        {
            var keys = new object[8];
            var values = new object[8];
            int count = 0;
            long length = t.Length;
            bool sequence = allowList && length > 0;
            foreach (LuaPair p in t.Pairs())
            {
                LuaType kt = p.KeyType;
                object key;
                if (kt == LuaType.String)
                {
                    key = p.Key.AsString();
                }
                else if (kt == LuaType.Integer || kt == LuaType.Number || kt == LuaType.Boolean)
                {
                    LuaValue k = p.Key;
                    key = kt == LuaType.Integer ? (object)k.AsLong() : kt == LuaType.Number ? (object)k.AsDouble() : (object)k.AsBool();
                }
                else
                {
                    throw new InvalidOperationException("ToDictionary: a key that is a " + LuaValue.Name(kt) + " has no text");
                }
                if (!(key is long) || (long)key < 1 || (long)key > length)
                {
                    sequence = false;
                }
                if (count == keys.Length)
                {
                    keys = Grow(keys);
                    values = Grow(values);
                }
                keys[count] = key;
                values[count] = ElementObject(p, path, depth);
                count++;
            }
            if (sequence && count == length)
            {
                var list = new object[length];
                for (int i = 0; i < count; i++)
                {
                    list[(long)keys[i] - 1] = values[i];
                }
                return new List<object>(list);
            }
            var dict = new Dictionary<string, object>(count);
            for (int i = 0; i < count; i++)
            {
                object k = keys[i];
                string text = k as string ?? (k is double ? ((double)k).ToString("R", CultureInfo.InvariantCulture)
                    : k is bool ? ((bool)k ? "true" : "false") : ((long)k).ToString(CultureInfo.InvariantCulture));
                dict[text] = values[i];
            }
            return dict;
        }

        static object[] Grow(object[] a)
        {
            var b = new object[a.Length * 2];
            Array.Copy(a, b, a.Length);
            return b;
        }

        object ElementObject(LuaPair p, int[] path, int depth)
        {
            LuaValue v = p.Value;
            switch (v.Type)
            {
                case LuaType.Boolean: return v.AsBool();
                case LuaType.Integer: return v.AsLong();
                case LuaType.Number: return v.AsDouble();
                case LuaType.String: return v.AsString();
                case LuaType.Buffer: return v.AsBuffer().ToArray();
                case LuaType.Table:
                    {
                        LuaTableRef borrowed = v.AsTable();
                        int native = NativeHandle(borrowed.Slot, borrowed.Generation, LuaType.Table);
                        for (int i = 0; i < depth; i++)
                        {
                            if (NativeEqual(path[i], native))
                            {
                                throw new InvalidOperationException("ToDictionary: the table refers to itself (a cycle)");
                            }
                        }
                        if (depth >= path.Length)
                        {
                            throw new InvalidOperationException("ToDictionary: tables nested deeper than maxDepth (" + path.Length + ")");
                        }
                        using (LuaTableRef nested = borrowed.Retain())
                        {
                            path[depth] = slots[nested.Slot].nativeRef;
                            return TableToObject(nested, path, depth + 1, true);
                        }
                    }
                default:
                    return LuaValue.Name(v.Type);
            }
        }

        // Whether two native handles refer to the same value.
        bool NativeEqual(int a, int b)
        {
            if (a == b)
            {
                return true;
            }
            xlua_value* v = stackalloc xlua_value[4];
            v[0].i = a;
            v[1].i = b;
            Op(XLUA_HANDLE_EQUAL, v);
            return v[2].i != 0;
        }

        void CloseHandles()
        {
            slots = GlobalsSlot();
            usedSlots = 1;
            freeSlotCount = 0;
            liveHandles = 0;
            iters = new IterState[0];
            iterCount = 0;
            keys = null;
            globalsRef = 0;
        }
    }
}
#endif
