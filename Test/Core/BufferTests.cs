/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// The buffer tests of LuaScript (docs/design/core-luascript.md, sections 2.10
// and 7.2; milestone 10): LuaLibs.Buffer, LuaBufferRef and buffers in calls.
// The library has them when it was built with CMake XLUA_BUFFER (on in the core
// preset); XLUA_EXPECT_BUFFER=ON or OFF says which build it is, and unset the
// library decides. Without the library every test checks that buffers are
// refused, so the test count stays the same.
using System;
using System.Text;
using XLua;

public static partial class CoreTests
{
    const LuaLibs WithBuffer = LuaLibs.Safe | LuaLibs.Buffer;

    static bool? bufferBuilt;

    // Whether the native library has the buffer library.
    static bool BufferBuilt()
    {
        if (bufferBuilt == null)
        {
            bool built;
            try
            {
                new LuaScript(new LuaOptions { Libraries = WithBuffer }).Dispose();
                built = true;
            }
            catch (NotSupportedException)
            {
                built = false;
            }
            string expect = (Environment.GetEnvironmentVariable("XLUA_EXPECT_BUFFER") ?? "").ToUpperInvariant();
            Check(expect != "ON" || built, "XLUA_EXPECT_BUFFER=ON, but LuaLibs.Buffer is refused");
            Check(expect != "OFF" || !built, "XLUA_EXPECT_BUFFER=OFF, but LuaLibs.Buffer opens");
            bufferBuilt = built;
        }
        return bufferBuilt.Value;
    }

    // A script with the buffer library, or null (after checking the refusal) without it.
    static LuaScript OpenBuffer(LuaOptions options = null)
    {
        options = options ?? new LuaOptions();
        options.Libraries = WithBuffer;
        if (!BufferBuilt())
        {
            Throws<NotSupportedException>(() => new LuaScript(options), "LuaLibs.Buffer without the buffer library");
            using (var lua = Open())
            {
                Throws<NotSupportedException>(() => lua.NewBuffer(4), "NewBuffer without the buffer library");
            }
            Console.WriteLine("  no buffer library (XLUA_BUFFER=OFF): checked the refusal");
            return null;
        }
        return new LuaScript(options);
    }

    const string BufferFunctions = @"
function make(n) local b = buffer.create(n) for i = 0, n - 1 do buffer.writeu8(b, i, i) end return b end
function len(b) return buffer.len(b) end
function u32(b, off) return buffer.readu32(b, off) end
function f64(b, off) return buffer.readf64(b, off) end
function put(b) buffer.writei32(b, 0, -2) buffer.writef32(b, 4, 1.5) buffer.writef64(b, 8, -0.25) buffer.writeu16(b, 16, 0xBEEF) end
function same(a, b) return rawequal(a, b) end
function keep(b) kept = b end
function kept_u8(i) return buffer.readu8(kept, i) end
function two() local b = buffer.create(3) return b, b end
function mixed() return 7, buffer.create(2), 'x' end
function none() end
";

    public static void TestBufferLibrary()
    {
        using (var lua = Open())
        {
            Equal(true, lua.Load<bool>("return buffer == nil", "default"), "LuaLibs.Buffer is not in the default set");
        }
        using (var lua = new LuaScript(new LuaOptions { Libraries = LuaLibs.Safe }))
        {
            Equal(true, lua.Load<bool>("return buffer == nil", "safe"), "LuaLibs.Safe has no buffer");
        }
        var e = Throws<NotSupportedException>(() => Open().NewBuffer(4), "NewBuffer without LuaLibs.Buffer");
        if (e != null) Contains(e.Message, "LuaLibs.Buffer", "the hint names the library");
        using (var lua = OpenBuffer())
        {
            if (lua == null) return;
            Equal(true, lua.Load<bool>("return type(buffer.create) == 'function' and getmetatable(buffer.create(1)) == false", "lib"),
                "LuaLibs.Buffer opens the locked library");
            using (var only = new LuaScript(new LuaOptions { Libraries = LuaLibs.Buffer }))
            {
                Equal(true, only.Load<bool>("return string == nil and buffer.len(buffer.create(3)) == 3", "only"), "the buffer library alone");
            }
        }
    }

    public static void TestBufferFromLua()
    {
        using (var lua = OpenBuffer())
        {
            if (lua == null) return;
            lua.Load(BufferFunctions, "buffers");
            using (LuaBufferRef b = lua.Call<LuaBufferRef>("make", 8))
            {
                Check(b.IsValid && b.Script == lua, "a buffer result is a valid handle");
                Equal(8, b.Length, "Length");
                Equal(1, lua.LiveHandles, "one live handle");
                Equal(0x03020100, b.ReadInt32(0), "little-endian ReadInt32");
                Equal((ushort)0x0504, b.ReadUInt16(4), "ReadUInt16");
                Equal((byte)7, b.ReadByte(7), "ReadByte");
                var copy = new byte[8];
                b.CopyTo(copy);
                Equal("00-01-02-03-04-05-06-07", BitConverter.ToString(copy), "CopyTo");
                Equal("00-01-02-03-04-05-06-07", BitConverter.ToString(b.ToArray()), "ToArray");
                var part = new byte[3];
                b.CopyTo(5, part);
                Equal("05-06-07", BitConverter.ToString(part), "CopyTo at an offset");
                // A buffer passed back to Lua is the same buffer.
                Equal(8L, lua.Call<long>("len", b), "a buffer argument");
                Equal(true, lua.Call<bool>("same", b, b), "the same buffer twice");
                lua.Call("keep", b);
            }
            Equal(0, lua.LiveHandles, "disposed");
            // Lua still holds it: the bytes survive the handle.
            lua.CollectGarbage();
            Equal(7L, lua.Call<long>("kept_u8", 7), "Lua keeps the buffer after the handle is disposed");
            // nil reads as an invalid handle; another type is an error.
            LuaBufferRef none = lua.Call<LuaBufferRef>("none");
            Check(!none.IsValid && none.Script == null, "nil is default(LuaBufferRef)");
            var te = Throws<LuaTypeException>(() => lua.Call<LuaBufferRef>("len", lua.Call<LuaBufferRef>("make", 1)), "a number read as a buffer");
            if (te != null) Contains(te.Message, "buffer expected, got integer", "the type error");
        }
    }

    public static void TestBufferLayoutAndSpan()
    {
        using (var lua = OpenBuffer())
        {
            if (lua == null) return;
            lua.Load(BufferFunctions, "buffers");
            using (LuaBufferRef b = lua.NewBuffer(24))
            {
                Equal(24, b.Length, "NewBuffer length");
                Equal("00-00-00-00", BitConverter.ToString(b.ToArray(), 0, 4), "zero-filled");
                // C# writes, Lua reads: little-endian, unaligned.
                b.WriteUInt32(1, 0xA1B2C3D4);
                Equal(0xA1B2C3D4L, lua.Call<long>("u32", b, 1), "Lua reads what C# wrote (u32 is a Lua integer)");
                Equal((byte)0xD4, b.ReadByte(1), "the low byte first");
                b.WriteDouble(9, Math.PI);
                Equal(Math.PI, lua.Call<double>("f64", b, 9), "an unaligned double");
                // Lua writes, C# reads.
                lua.Call("put", b);
                Equal(-2, b.ReadInt32(0), "ReadInt32 of Lua's writei32");
                Equal(4294967294u, b.ReadUInt32(0), "ReadUInt32");
                Equal(1.5f, b.ReadSingle(4), "ReadSingle of writef32");
                Equal(-0.25, b.ReadDouble(8), "ReadDouble of writef64");
                Equal((short)-16657, b.ReadInt16(16), "ReadInt16 of writeu16 0xBEEF");
                Equal((sbyte)-17, b.ReadSByte(16), "ReadSByte");
                // AsSpan is Lua's memory.
                Span<byte> span = b.AsSpan();
                Equal(24, span.Length, "the span covers the buffer");
                Equal((byte)0xEF, span[16], "the span sees Lua's writes");
                span[20] = 0x42;
                lua.Call("keep", b);
                Equal(0x42L, lua.Call<long>("kept_u8", 20), "Lua sees writes through the span");
                b.CopyFrom(Encoding.ASCII.GetBytes("abc"), 21);
                Equal("abc", lua.Load<string>("return buffer.readstring(kept, 21, 3)", "read"), "CopyFrom is seen by Lua");
                b.WriteSingle(0, float.NaN);
                Equal(true, lua.Load<bool>("local x = buffer.readf32(kept, 0) return x ~= x", "nan"), "NaN through the buffer");
                b.WriteSingle(0, float.PositiveInfinity);
                Equal(true, lua.Load<bool>("return buffer.readf32(kept, 0) == math.huge", "inf"), "infinity through the buffer");
                b.WriteSByte(0, -1); b.WriteByte(1, 2); b.WriteInt16(2, -2); b.WriteUInt16(4, 65535); b.WriteInt32(6, int.MinValue);
                Equal("FF-02-FE-FF-FF-FF-00-00-00-80", BitConverter.ToString(b.ToArray(), 0, 10), "the write widths");
            }
            using (LuaBufferRef c = lua.NewBuffer(Encoding.ASCII.GetBytes("hello")))
            {
                Equal(5L, lua.Call<long>("len", c), "NewBuffer(span)");
                lua.Call("keep", c);
                Equal("hello", lua.Load<string>("return buffer.tostring(kept)", "s"), "the contents");
            }
            using (LuaBufferRef empty = lua.NewBuffer(0))
            {
                Equal(0, empty.Length, "an empty buffer");
                Equal(0, empty.AsSpan().Length, "its span");
                Throws<ArgumentOutOfRangeException>(() => empty.ReadByte(0), "nothing to read");
            }
        }
    }

    public static void TestBufferBounds()
    {
        using (var lua = OpenBuffer())
        {
            if (lua == null) return;
            Throws<ArgumentOutOfRangeException>(() => lua.NewBuffer(-1), "a negative length");
            Throws<ArgumentOutOfRangeException>(() => lua.NewBuffer(LuaScript.MaxBufferLength + 1), "more than 1 GiB");
            using (LuaBufferRef b = lua.NewBuffer(8))
            {
                LuaBufferRef h = b;
                Throws<ArgumentOutOfRangeException>(() => h.ReadInt32(5), "ReadInt32 past the end");
                Throws<ArgumentOutOfRangeException>(() => h.ReadInt32(-1), "a negative offset");
                Throws<ArgumentOutOfRangeException>(() => h.ReadDouble(1), "ReadDouble past the end");
                Throws<ArgumentOutOfRangeException>(() => h.ReadByte(8), "ReadByte at the length");
                Throws<ArgumentOutOfRangeException>(() => h.WriteUInt16(7, 1), "WriteUInt16 past the end");
                Throws<ArgumentOutOfRangeException>(() => h.WriteInt32(int.MaxValue, 1), "a huge offset");
                Throws<ArgumentOutOfRangeException>(() => h.CopyFrom(new byte[9]), "CopyFrom too long");
                Throws<ArgumentOutOfRangeException>(() => h.CopyFrom(new byte[2], 7), "CopyFrom past the end");
                Throws<ArgumentOutOfRangeException>(() => h.CopyTo(6, new byte[3]), "CopyTo past the end");
                Throws<ArgumentException>(() => h.CopyTo(new byte[7]), "CopyTo into a short span");
                h.CopyFrom(new byte[0], 8);
                Equal(0.0, h.ReadDouble(0), "an access that fits");
            }
            var e = Throws<LuaRuntimeException>(() => lua.Load("buffer.readu32(buffer.create(16), 13)", "oob"), "Lua's bounds error");
            if (e != null) Contains(e.Message, "buffer access out of bounds (offset 13 and 4 bytes in a buffer of 16 bytes)", "the message");
        }
    }

    public static void TestBufferHandles()
    {
        using (var lua = OpenBuffer())
        {
            if (lua == null) return;
            lua.Load(BufferFunctions, "buffers");
            LuaBufferRef b = lua.NewBuffer(4);
            LuaBufferRef copy = b;
            Check(b.Equals(copy) && b == copy, "a copy is equal");
            b.Dispose();
            Check(!b.IsValid && !copy.IsValid, "a disposed handle and its copies are invalid");
            Throws<ObjectDisposedException>(() => { int n = copy.Length; }, "Length of a disposed handle");
            Throws<ObjectDisposedException>(() => copy.AsSpan(), "AsSpan of a disposed handle");
            Throws<ObjectDisposedException>(() => copy.ReadInt32(0), "a read of a disposed handle");
            Throws<ObjectDisposedException>(() => lua.Call("len", copy), "a disposed handle as an argument");
            copy.Dispose();  // a no-op
            Equal(0, lua.LiveHandles, "no live handle");
            // A new handle in the same slot has another generation.
            using (LuaBufferRef again = lua.NewBuffer(4))
            {
                Check(again.IsValid && !copy.IsValid && again != copy, "the slot's new handle does not revive the old one");
            }
            Throws<ObjectDisposedException>(() => default(LuaBufferRef).ReadByte(0), "default(LuaBufferRef)");
            Check(!default(LuaBufferRef).IsValid, "default is invalid");
            default(LuaBufferRef).Dispose();
            // Two handles to one buffer are equal; two buffers are not.
            var two = lua.Call<LuaBufferRef, LuaBufferRef>("two", ReadOnlySpan<LuaValue>.Empty);
            Check(two.Item1 == two.Item2 && two.Item1.IsValid, "two handles to one buffer are equal");
            using (LuaBufferRef other = lua.NewBuffer(3))
            {
                Check(two.Item1 != other, "another buffer is not equal");
            }
            two.Item1.Dispose();
            Check(two.Item2.IsValid, "the other handle stays valid");
            two.Item2.Dispose();
            // LuaValue holds a buffer handle.
            using (LuaBufferRef v = lua.NewBuffer(5))
            {
                LuaValue value = v;
                Equal(LuaType.Buffer, value.Type, "LuaValue of a buffer");
                Check(value.AsBuffer() == v && value == (LuaValue)v, "AsBuffer and equality");
                Equal(5L, lua.Call<long>("len", value), "a LuaValue buffer argument");
                Equal("buffer", value.ToString(), "ToString");
                Throws<LuaTypeException>(() => ((LuaValue)1).AsBuffer(), "AsBuffer of a number");
                Check(((LuaValue)default(LuaBufferRef)).IsNil, "default(LuaBufferRef) is nil");
            }
            // Results as LuaValue: owning handles the caller disposes.
            var results = new LuaValue[3];
            Equal(3, lua.Call("mixed", ReadOnlySpan<LuaValue>.Empty, results), "three results");
            Equal(LuaType.Buffer, results[1].Type, "a buffer among the results");
            Equal(1, lua.LiveHandles, "the buffer result is a live handle");
            Equal(2, results[1].AsBuffer().Length, "its length");
            results[1].AsBuffer().Dispose();
            LuaValue single = lua.Call<LuaValue>("make", 2);
            Equal(LuaType.Buffer, single.Type, "Call<LuaValue> of a buffer");
            single.AsBuffer().Dispose();
            Equal(0, lua.LiveHandles, "all released");
            // A buffer of another script.
            using (var other = OpenBuffer())
            using (LuaBufferRef foreign = other.NewBuffer(1))
            {
                var ae = Throws<ArgumentException>(() => lua.Call("len", foreign), "a buffer of another script");
                if (ae != null) Contains(ae.Message, "another LuaScript", "the message");
            }
            // Disposing the script invalidates every handle.
            LuaBufferRef last = lua.NewBuffer(1);
            lua.Dispose();
            Check(!last.IsValid, "invalid after the script is disposed");
            Throws<ObjectDisposedException>(() => last.ReadByte(0), "a read after the script is disposed");
            last.Dispose();  // a no-op
        }
    }

    public static void TestBufferResultsAreReleased()
    {
        using (var lua = OpenBuffer(new LuaOptions { MemoryLimit = 32 << 20 }))
        {
            if (lua == null) return;
            lua.Load(BufferFunctions + "function big() return buffer.create(1 << 20) end\n"
                + "function bigs() return buffer.create(1 << 20), buffer.create(1 << 20), 1 end", "buffers");
            lua.CollectGarbage();
            long before = lua.MemoryUsed;
            for (int i = 0; i < 100; i++)
            {
                Throws<LuaTypeException>(() => lua.Call<long>("big"), "a buffer read as long");
                Equal(true, lua.Call<bool>("big"), "a buffer read as bool (Lua truth)");
                Throws<LuaTypeException>(() => lua.Call<string>("big"), "a buffer read as string");
                lua.Call("big");
                Throws<LuaTypeException>(() => lua.Call<LuaBufferRef, long>("bigs", ReadOnlySpan<LuaValue>.Empty), "a tuple read failing on the second");
                Throws<LuaTypeException>(() => lua.Call<long, LuaBufferRef, LuaBufferRef>("bigs", ReadOnlySpan<LuaValue>.Empty), "a tuple read failing on the first");
                Throws<NotSupportedException>(() => lua.Call<DateTime>("big"), "an unsupported type");
            }
            Equal(0, lua.LiveHandles, "no handle survives a failed read");
            lua.CollectGarbage();
            Check(lua.MemoryUsed < before + (1 << 20), "the native handles were released: " + (lua.MemoryUsed - before) + " bytes more");
        }
    }

    public static void TestBufferLimits()
    {
        using (var lua = OpenBuffer(new LuaOptions { MemoryLimit = 8 << 20 }))
        {
            if (lua == null) return;
            var e = Throws<LuaMemoryLimitException>(() => lua.NewBuffer(16 << 20), "NewBuffer over the memory limit");
            if (e != null) Check(e.MemoryLimit == 8 << 20 && e.MemoryUsed <= (8 << 20) + 16 * 1024, "within the limit");
            Throws<LuaMemoryLimitException>(() => lua.Load("local b = buffer.create(16 << 20)", "big"), "buffer.create over the limit");
            Equal("caught", lua.Load<string>("return pcall(buffer.create, 1 << 30) and 'no' or 'caught'", "pcall"), "pcall catches it");
            lua.CollectGarbage();
            long used = lua.MemoryUsed;
            using (LuaBufferRef b = lua.NewBuffer(4 << 20))
            {
                // A collection step during the allocation may free a little (the string table shrinks).
                Check(lua.MemoryUsed >= used + (4 << 20) - 4096, "MemoryUsed counts the buffer");
            }
            lua.CollectGarbage();
            Check(lua.MemoryUsed < used + 64 * 1024, "a released buffer is collected");
        }
        using (var lua = OpenBuffer(new LuaOptions { MemoryLimit = 64 << 20, InstructionLimit = 1000000 }))
        {
            if (lua == null) return;
            // One instruction-equivalent per 32 bytes: a 1 MiB fill costs 32,768.
            lua.Load("b = buffer.create(1 << 20)", "make");
            lua.Load("for i = 1, 10 do buffer.fill(b, 0, i) end", "fill");
            long tenFills = lua.LastInstructionCount;
            Check(tenFills >= 10 * 32768 && tenFills < 10 * 32768 + 1000, "ten 1 MiB fills cost about 327,680: " + tenFills);
            var le = Throws<LuaInstructionLimitException>(() => lua.Load("for i = 1, 1000 do buffer.fill(b, 0, i) end", "fills"), "fills over the limit");
            if (le != null) Check(lua.LastInstructionCount <= 1000000 + 4096, "stopped near the limit: " + lua.LastInstructionCount);
            Throws<LuaInstructionLimitException>(() => lua.Load("for i = 1, 1000 do pcall(buffer.copy, b, 0, b, 1) end", "copies"), "copies inside pcall");
            // NewBuffer from C# is not charged.
            lua.InstructionLimit = 10;
            using (LuaBufferRef big = lua.NewBuffer(8 << 20))
            {
                Equal(8 << 20, big.Length, "NewBuffer under a tiny instruction limit");
            }
        }
        using (var lua = OpenBuffer(new LuaOptions { MemoryLimit = 64 << 20, TimeLimit = TimeSpan.FromMilliseconds(100) }))
        {
            if (lua == null) return;
            lua.Load("b = buffer.create(16 << 20)", "make");
            var start = DateTime.UtcNow;
            Throws<LuaTimeLimitException>(() => lua.Load("while true do buffer.fill(b, 0, 1) end", "spin"), "fills under a time limit");
            Check(DateTime.UtcNow - start < TimeSpan.FromSeconds(5), "stopped in time");
        }
    }

    public static void TestBufferCallsAllocateNothing()
    {
        long probe = Allocated();
        var keep = new byte[1000];
        if (Allocated() - probe < 1000)
        {
            Console.WriteLine("  GC.GetAllocatedBytesForCurrentThread does not count on this runtime; skipped");
            using (OpenBuffer()) { }
            return;
        }
        using (var lua = OpenBuffer(new LuaOptions { MemoryLimit = 16 << 20 }))
        {
            if (lua == null) return;
            lua.Load(BufferFunctions, "buffers");
            for (int round = 0; round < 2; round++)  // the first round grows the handle table and warms up the JIT
            {
                long before = Allocated();
                long sum = 0;
                for (int i = 0; i < 2000; i++)
                {
                    using (LuaBufferRef b = lua.Call<LuaBufferRef>("make", 16))
                    {
                        sum += b.ReadInt32(0) + b.ReadByte(15) + (long)b.ReadDouble(8);
                        b.WriteUInt32(4, (uint)i);
                        sum += lua.Call<long>("u32", b, 4);
                        sum += b.AsSpan()[3];
                    }
                    using (LuaBufferRef n = lua.NewBuffer(8))
                    {
                        sum += lua.Call<long>("len", n);
                    }
                }
                long allocated = Allocated() - before;
                if (round == 1)
                {
                    Console.WriteLine("  allocation probe: " + allocated + " bytes for 6,000 calls with buffers");
                    Equal(0L, allocated, "bytes allocated by buffer handles and accessors");
                }
                Check(sum > 0, "the calls ran");
            }
        }
        GC.KeepAlive(keep);
    }
}
