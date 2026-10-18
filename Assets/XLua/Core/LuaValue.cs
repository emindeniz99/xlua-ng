/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// LuaScript's values (docs/design/core-luascript.md, section 2.4). Compiled with
// the scripting define XLUA_LUASCRIPT or XLUA_CORE_ONLY, like LuaScript.
#if XLUA_LUASCRIPT || XLUA_CORE_ONLY
namespace XLua
{
    using System;
    using System.Globalization;
    using System.Text;

    /// <summary>The type of a <see cref="LuaValue"/>; the numbers are xlua_value's (build/xlua_core.h).</summary>
    public enum LuaType : byte
    {
        Nil = 0,
        Boolean = 1,
        Integer = 2,
        Number = 3,
        String = 4,
        Table = 5,
        Function = 6,
        Buffer = 7,
        Other = 8,
    }

    /// <summary>
    /// One Lua value without boxing: nil, a boolean, a 64-bit integer, a
    /// float, or a string (a C# string, or bytes from <see cref="FromBytes"/>).
    /// Numbers and booleans convert implicitly, so <c>lua.Call("f", 1, 2.5, true)</c>
    /// allocates nothing. Tables, functions and other values that a call
    /// returns carry only their <see cref="Type"/> for now; their handles
    /// (LuaTableRef, LuaFunctionRef) come with milestone 8, and such a value
    /// cannot be passed back into Lua.
    /// </summary>
    public readonly struct LuaValue : IEquatable<LuaValue>
    {
        readonly object obj;   // string or byte[] for strings
        readonly long bits;    // an integer, a boolean (0 or 1) or a double's bits
        readonly LuaType type;

        public static readonly LuaValue Nil = default(LuaValue);

        LuaValue(LuaType type, long bits, object obj)
        {
            this.type = type;
            this.bits = bits;
            this.obj = obj;
        }

        public LuaType Type { get { return type; } }
        public bool IsNil { get { return type == LuaType.Nil; } }

        public static implicit operator LuaValue(bool v) { return new LuaValue(LuaType.Boolean, v ? 1 : 0, null); }
        // Every integral type has its own operator: with only long and double,
        // a ulong would convert through double and lose bits.
        public static implicit operator LuaValue(sbyte v) { return new LuaValue(LuaType.Integer, v, null); }
        public static implicit operator LuaValue(byte v) { return new LuaValue(LuaType.Integer, v, null); }
        public static implicit operator LuaValue(short v) { return new LuaValue(LuaType.Integer, v, null); }
        /// <summary>char converts through ushort: its code unit.</summary>
        public static implicit operator LuaValue(ushort v) { return new LuaValue(LuaType.Integer, v, null); }
        public static implicit operator LuaValue(int v) { return new LuaValue(LuaType.Integer, v, null); }
        public static implicit operator LuaValue(uint v) { return new LuaValue(LuaType.Integer, v, null); }
        public static implicit operator LuaValue(long v) { return new LuaValue(LuaType.Integer, v, null); }
        /// <summary>Lua has no unsigned integers: a value above long.MaxValue is its two's-complement long.</summary>
        public static implicit operator LuaValue(ulong v) { return new LuaValue(LuaType.Integer, unchecked((long)v), null); }
        public static implicit operator LuaValue(float v) { return new LuaValue(LuaType.Number, BitConverter.DoubleToInt64Bits(v), null); }
        public static implicit operator LuaValue(double v) { return new LuaValue(LuaType.Number, BitConverter.DoubleToInt64Bits(v), null); }
        /// <summary>null is nil.</summary>
        public static implicit operator LuaValue(string v) { return v == null ? Nil : new LuaValue(LuaType.String, 0, v); }

        /// <summary>A Lua string with these bytes (any bytes); null is nil. The array is not copied.</summary>
        public static LuaValue FromBytes(byte[] bytes)
        {
            return bytes == null ? Nil : new LuaValue(LuaType.String, 0, bytes);
        }

        // A table, function or other value a call returned: only its type.
        internal static LuaValue OfType(LuaType type)
        {
            return new LuaValue(type, 0, null);
        }

        internal object Object { get { return obj; } }
        internal long Bits { get { return bits; } }

        /// <summary>Lua's truth: only nil and false are false.</summary>
        public bool AsBool()
        {
            return !(type == LuaType.Nil || (type == LuaType.Boolean && bits == 0));
        }

        /// <summary>An integer, or a float with an exact integer value; else <see cref="LuaTypeException"/>.</summary>
        public long AsLong()
        {
            long v;
            if (!TryGetLong(out v))
            {
                throw TypeError(LuaType.Integer, this);
            }
            return v;
        }

        /// <summary>An integer or a float; else <see cref="LuaTypeException"/>.</summary>
        public double AsDouble()
        {
            double v;
            if (!TryGetDouble(out v))
            {
                throw TypeError(LuaType.Number, this);
            }
            return v;
        }

        /// <summary>A string (bytes decoded as UTF-8, U+FFFD for invalid ones), or null for nil; numbers are not converted.</summary>
        public string AsString()
        {
            string v;
            if (type == LuaType.Nil)
            {
                return null;
            }
            if (!TryGetString(out v))
            {
                throw TypeError(LuaType.String, this);
            }
            return v;
        }

        /// <summary>The bytes of a string (a new array), or null for nil.</summary>
        public byte[] AsBytes()
        {
            if (type == LuaType.Nil)
            {
                return null;
            }
            if (type != LuaType.String)
            {
                throw TypeError(LuaType.String, this);
            }
            var bytes = obj as byte[];
            return bytes != null ? (byte[])bytes.Clone() : Encoding.UTF8.GetBytes((string)obj);
        }

        public bool TryGetLong(out long v)
        {
            if (type == LuaType.Integer)
            {
                v = bits;
                return true;
            }
            if (type == LuaType.Number)
            {
                return LuaScript.FloatToLong(BitConverter.Int64BitsToDouble(bits), out v);
            }
            v = 0;
            return false;
        }

        public bool TryGetDouble(out double v)
        {
            if (type == LuaType.Integer)
            {
                v = bits;
                return true;
            }
            if (type == LuaType.Number)
            {
                v = BitConverter.Int64BitsToDouble(bits);
                return true;
            }
            v = 0;
            return false;
        }

        public bool TryGetString(out string v)
        {
            if (type == LuaType.String)
            {
                var bytes = obj as byte[];
                v = bytes != null ? Encoding.UTF8.GetString(bytes) : (string)obj;
                return true;
            }
            v = null;
            return false;
        }

        internal static LuaTypeException TypeError(LuaType expected, LuaValue actual)
        {
            return TypeError(expected, actual.type, actual.type == LuaType.Number ? BitConverter.Int64BitsToDouble(actual.bits) : 0);
        }

        internal static LuaTypeException TypeError(LuaType expected, LuaType actual, double number)
        {
            string got = actual == LuaType.Number && expected == LuaType.Integer
                ? "number has no integer representation (" + number.ToString("R", CultureInfo.InvariantCulture) + ")"
                : Name(expected) + " expected, got " + Name(actual);
            return new LuaTypeException(got, expected, actual);
        }

        internal static string Name(LuaType type)
        {
            switch (type)
            {
                case LuaType.Nil: return "nil";
                case LuaType.Boolean: return "boolean";
                case LuaType.Integer: return "integer";
                case LuaType.Number: return "number";
                case LuaType.String: return "string";
                case LuaType.Table: return "table";
                case LuaType.Function: return "function";
                case LuaType.Buffer: return "buffer";
                default: return "userdata";
            }
        }

        /// <summary>
        /// Lua's raw equality for the values a LuaValue holds: an integer
        /// equals a float of the same value, strings compare by their bytes.
        /// Tables, functions and other values without a handle are never equal.
        /// </summary>
        public bool Equals(LuaValue other)
        {
            if (type == LuaType.Integer || type == LuaType.Number)
            {
                if (other.type != LuaType.Integer && other.type != LuaType.Number)
                {
                    return false;
                }
                if (type == LuaType.Integer && other.type == LuaType.Integer)
                {
                    return bits == other.bits;
                }
                if (type == LuaType.Number && other.type == LuaType.Number)
                {
                    return BitConverter.Int64BitsToDouble(bits) == BitConverter.Int64BitsToDouble(other.bits);
                }
                long i;
                double d = type == LuaType.Number ? BitConverter.Int64BitsToDouble(bits) : BitConverter.Int64BitsToDouble(other.bits);
                return LuaScript.FloatToLong(d, out i) && i == (type == LuaType.Integer ? bits : other.bits);
            }
            if (type != other.type)
            {
                return false;
            }
            switch (type)
            {
                case LuaType.Nil:
                    return true;
                case LuaType.Boolean:
                    return bits == other.bits;
                case LuaType.String:
                    if (obj is string && other.obj is string)
                    {
                        return string.Equals((string)obj, (string)other.obj, StringComparison.Ordinal);
                    }
                    return BytesEqual(BytesOf(obj), BytesOf(other.obj));
                default:
                    return false;
            }
        }

        static byte[] BytesOf(object o)
        {
            return o as byte[] ?? Encoding.UTF8.GetBytes((string)o);
        }

        static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is LuaValue && Equals((LuaValue)obj);
        }

        public override int GetHashCode()
        {
            switch (type)
            {
                case LuaType.Integer:
                    return bits.GetHashCode();
                case LuaType.Number:
                    {
                        double d = BitConverter.Int64BitsToDouble(bits);
                        long i;
                        return LuaScript.FloatToLong(d, out i) ? i.GetHashCode() : d.GetHashCode();
                    }
                case LuaType.Boolean:
                    return (int)bits + 1;
                case LuaType.String:
                    {
                        // By bytes, so that a string and FromBytes of its UTF-8 agree.
                        byte[] b = BytesOf(obj);
                        int h = unchecked((int)2166136261);
                        for (int i = 0; i < b.Length; i++)
                        {
                            h = (h ^ b[i]) * 16777619;
                        }
                        return h;
                    }
                default:
                    return (int)type;
            }
        }

        public static bool operator ==(LuaValue a, LuaValue b) { return a.Equals(b); }
        public static bool operator !=(LuaValue a, LuaValue b) { return !a.Equals(b); }

        /// <summary>For debugging: the value as Lua's tostring would show it (no addresses).</summary>
        public override string ToString()
        {
            switch (type)
            {
                case LuaType.Nil:
                    return "nil";
                case LuaType.Boolean:
                    return bits != 0 ? "true" : "false";
                case LuaType.Integer:
                    return bits.ToString(CultureInfo.InvariantCulture);
                case LuaType.Number:
                    return BitConverter.Int64BitsToDouble(bits).ToString("R", CultureInfo.InvariantCulture);
                case LuaType.String:
                    {
                        string s;
                        TryGetString(out s);
                        return s;
                    }
                default:
                    return Name(type);
            }
        }
    }
}
#endif
