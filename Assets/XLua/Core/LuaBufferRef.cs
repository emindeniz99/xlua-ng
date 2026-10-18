/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// LuaScript's buffers (docs/design/core-luascript.md, section 2.10; milestone
// 10): LuaBufferRef, a handle to a buffer of the buffer library
// (build/xlua_core_buffer.c), and the part of LuaScript that creates, adopts and
// releases buffer handles. Compiled with the scripting define XLUA_LUASCRIPT or
// XLUA_CORE_ONLY, like LuaScript.
#if XLUA_LUASCRIPT || XLUA_CORE_ONLY
namespace XLua
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using XLua.LuaDLL;
    using LuaAPI = XLua.LuaDLL.Lua;

    /// <summary>
    /// A handle to a Lua buffer (<c>LuaLibs.Buffer</c>): a fixed-size array of
    /// bytes that Lua's collector never moves. The handle keeps the buffer
    /// alive until <see cref="Dispose"/>, or until the script is disposed. It
    /// is a struct: creating one allocates nothing on the managed heap, and a
    /// forgotten handle keeps its buffer until the script is disposed
    /// (<see cref="LuaScript.LiveHandles"/> counts them). A copy of a disposed
    /// handle is invalid as well. Use it on the script's thread.
    /// </summary>
    public readonly struct LuaBufferRef : IDisposable, IEquatable<LuaBufferRef>
    {
        readonly LuaScript script;
        readonly int slot;
        readonly int generation;

        internal LuaBufferRef(LuaScript script, int slot, int generation)
        {
            this.script = script;
            this.slot = slot;
            this.generation = generation;
        }

        internal int Slot { get { return slot; } }
        internal int Generation { get { return generation; } }

        /// <summary>The script the buffer belongs to; null for default(LuaBufferRef).</summary>
        public LuaScript Script { get { return script; } }

        /// <summary>False after Dispose, for a copy of a disposed handle, after the script is disposed, and for default(LuaBufferRef).</summary>
        public bool IsValid { get { return script != null && script.BufferValid(slot, generation); } }

        /// <summary>The size in bytes; buffers never resize.</summary>
        public unsafe int Length
        {
            get
            {
                int length;
                Data(out length);
                return length;
            }
        }

        unsafe byte* Data(out int length)
        {
            if (script == null)
            {
                throw new ObjectDisposedException("LuaBufferRef", "default(LuaBufferRef) refers to no buffer");
            }
            return script.BufferData(slot, generation, out length);
        }

        unsafe byte* At(int offset, int size)
        {
            int length;
            byte* p = Data(out length);
            if (offset < 0 || offset > length - size)
            {
                throw new ArgumentOutOfRangeException("offset", offset, "a " + size + "-byte access is out of the bounds of a buffer of " + length + " bytes");
            }
            return p + offset;
        }

        /// <summary>
        /// The buffer's memory, without a copy. Lua writes are visible through
        /// it and its writes are visible to Lua. Valid only while this handle
        /// is valid: after <see cref="Dispose"/> (of this handle or of the
        /// script) the memory may be freed, which the span cannot detect. The
        /// copy methods are the safe default.
        /// </summary>
        public unsafe Span<byte> AsSpan()
        {
            int length;
            byte* p = Data(out length);
            return new Span<byte>(p, length);
        }

        /// <summary>Copies the whole buffer into destination, which must be at least <see cref="Length"/> bytes.</summary>
        public unsafe void CopyTo(Span<byte> destination)
        {
            int length;
            byte* p = Data(out length);
            new ReadOnlySpan<byte>(p, length).CopyTo(destination);
        }

        /// <summary>Copies destination.Length bytes from offset into destination.</summary>
        public unsafe void CopyTo(int offset, Span<byte> destination)
        {
            new ReadOnlySpan<byte>(At(offset, destination.Length), destination.Length).CopyTo(destination);
        }

        /// <summary>Copies source into the buffer at offset; the bytes must fit.</summary>
        public unsafe void CopyFrom(ReadOnlySpan<byte> source, int offset = 0)
        {
            source.CopyTo(new Span<byte>(At(offset, source.Length), source.Length));
        }

        /// <summary>A new array with the buffer's bytes.</summary>
        public byte[] ToArray()
        {
            var bytes = new byte[Length];
            CopyTo(bytes);
            return bytes;
        }

        // Little-endian, any alignment: as the buffer library reads and writes
        // (buffer.readi32 ...), whatever the host's byte order.
        static unsafe uint Load32(byte* p) { return (uint)(p[0] | p[1] << 8 | p[2] << 16 | p[3] << 24); }
        static unsafe void Store32(byte* p, uint v) { p[0] = (byte)v; p[1] = (byte)(v >> 8); p[2] = (byte)(v >> 16); p[3] = (byte)(v >> 24); }

        public unsafe sbyte ReadSByte(int offset) { return (sbyte)*At(offset, 1); }
        public unsafe byte ReadByte(int offset) { return *At(offset, 1); }
        public unsafe short ReadInt16(int offset) { byte* p = At(offset, 2); return (short)(p[0] | p[1] << 8); }
        public unsafe ushort ReadUInt16(int offset) { byte* p = At(offset, 2); return (ushort)(p[0] | p[1] << 8); }
        public unsafe int ReadInt32(int offset) { return (int)Load32(At(offset, 4)); }
        public unsafe uint ReadUInt32(int offset) { return Load32(At(offset, 4)); }
        public unsafe float ReadSingle(int offset) { uint bits = Load32(At(offset, 4)); return *(float*)&bits; }
        public unsafe double ReadDouble(int offset)
        {
            byte* p = At(offset, 8);
            ulong bits = Load32(p) | (ulong)Load32(p + 4) << 32;
            return *(double*)&bits;
        }

        public unsafe void WriteSByte(int offset, sbyte value) { *At(offset, 1) = (byte)value; }
        public unsafe void WriteByte(int offset, byte value) { *At(offset, 1) = value; }
        public unsafe void WriteInt16(int offset, short value) { byte* p = At(offset, 2); p[0] = (byte)value; p[1] = (byte)(value >> 8); }
        public unsafe void WriteUInt16(int offset, ushort value) { byte* p = At(offset, 2); p[0] = (byte)value; p[1] = (byte)(value >> 8); }
        public unsafe void WriteInt32(int offset, int value) { Store32(At(offset, 4), (uint)value); }
        public unsafe void WriteUInt32(int offset, uint value) { Store32(At(offset, 4), value); }
        public unsafe void WriteSingle(int offset, float value) { Store32(At(offset, 4), *(uint*)&value); }
        public unsafe void WriteDouble(int offset, double value)
        {
            byte* p = At(offset, 8);
            ulong bits = *(ulong*)&value;
            Store32(p, (uint)bits);
            Store32(p + 4, (uint)(bits >> 32));
        }

        /// <summary>Releases the handle; Lua frees the buffer once nothing else refers to it. A second Dispose, or one after the script's, does nothing.</summary>
        public void Dispose()
        {
            if (script != null)
            {
                script.ReleaseBuffer(slot, generation);
            }
        }

        /// <summary>The same buffer: the same script and memory (two handles may refer to one buffer); invalid handles are equal only to copies of themselves.</summary>
        public bool Equals(LuaBufferRef other)
        {
            if (script != other.script)
            {
                return false;
            }
            if (slot == other.slot && generation == other.generation)
            {
                return true;
            }
            return script != null && script.BufferValid(slot, generation) && script.BufferValid(other.slot, other.generation)
                && script.BufferPointer(slot) == script.BufferPointer(other.slot);
        }

        public override bool Equals(object obj)
        {
            return obj is LuaBufferRef && Equals((LuaBufferRef)obj);
        }

        public override int GetHashCode()
        {
            return script == null ? 0 : RuntimeHelpers.GetHashCode(script);
        }

        public static bool operator ==(LuaBufferRef a, LuaBufferRef b) { return a.Equals(b); }
        public static bool operator !=(LuaBufferRef a, LuaBufferRef b) { return !a.Equals(b); }

        public override string ToString()
        {
            return IsValid ? "buffer (" + Length + " bytes)" : "buffer (invalid handle)";
        }
    }

    // A reader of a result that may hold a handle: for LuaBufferRef and
    // LuaValue (the buffer case), chosen once per T like LuaRead<T>.
    internal delegate T LuaHandleReader<T>(LuaScript script, ref xlua_value v);

    internal static class LuaHandleRead<T>
    {
        internal static readonly LuaHandleReader<T> Read = (LuaHandleReader<T>)LuaHandleReaders.For(typeof(T));
    }

    internal static class LuaHandleReaders
    {
        internal static Delegate For(Type t)
        {
            if (t == typeof(LuaBufferRef)) return (LuaHandleReader<LuaBufferRef>)((LuaScript s, ref xlua_value v) => s.ReadBufferResult(ref v));
            if (t == typeof(LuaTableRef)) return (LuaHandleReader<LuaTableRef>)((LuaScript s, ref xlua_value v) => s.ReadTableResult(ref v));
            if (t == typeof(LuaFunctionRef)) return (LuaHandleReader<LuaFunctionRef>)((LuaScript s, ref xlua_value v) => s.ReadFunctionResult(ref v));
            if (t == typeof(LuaValue)) return (LuaHandleReader<LuaValue>)((LuaScript s, ref xlua_value v) => s.ResultValue(ref v));
            if (t == typeof(LuaCoroutine)) return (LuaHandleReader<LuaCoroutine>)((LuaScript s, ref xlua_value v) => s.ReadCoroutineResult(ref v));
            return null;
        }
    }

    public sealed unsafe partial class LuaScript
    {
        const int XLUA_T_BUFFER = 7;
        /// <summary>The largest buffer: 1 GiB (XLUA_BUFFER_MAX, Luau's limit).</summary>
        public const int MaxBufferLength = 1 << 30;

        /// <summary>
        /// A new zero-filled buffer of length bytes, as <c>buffer.create</c>
        /// makes it; dispose the handle. Needs <c>LuaLibs.Buffer</c> in
        /// <see cref="LuaOptions.Libraries"/> (else <see cref="NotSupportedException"/>);
        /// the memory counts against <see cref="MemoryLimit"/>
        /// (<see cref="LuaMemoryLimitException"/>).
        /// </summary>
        public LuaBufferRef NewBuffer(int length)
        {
            if (length < 0 || length > MaxBufferLength)
            {
                throw new ArgumentOutOfRangeException("length", length, "a buffer has 0 to " + MaxBufferLength + " bytes");
            }
            IntPtr state = Owned;
            xlua_value* v = stackalloc xlua_value[4];
            v[1].i = length;
            int status = LuaAPI.xlua_core_handle(state, XLUA_HANDLE_NEWBUFFER, v);
            if (status == XLUA_ERRMEM)
            {
                xlua_core_stats stats = Stats();
                throw new LuaMemoryLimitException("not enough memory for a buffer of " + length + " bytes", stats.memory_limit, stats.memory_used, false);
            }
            if (status != XLUA_OK)
            {
                throw new NotSupportedException("this script has no buffer library: open it with LuaOptions.Libraries = ... | LuaLibs.Buffer "
                    + "(the native library needs CMake XLUA_BUFFER, on in the core preset)");
            }
            int slot = AddHandle(LuaType.Buffer, (int)v[0].i, SlotOwned);
            slots[slot].data = v[2].s;
            slots[slot].length = length;
            return new LuaBufferRef(this, slot, slots[slot].generation);
        }

        /// <summary>A new buffer with a copy of contents.</summary>
        public LuaBufferRef NewBuffer(ReadOnlySpan<byte> contents)
        {
            LuaBufferRef b = NewBuffer(contents.Length);
            b.CopyFrom(contents);
            return b;
        }

        internal bool BufferValid(int slot, int generation)
        {
            return HandleValid(slot, generation, LuaType.Buffer);
        }

        // The bytes and length of a buffer slot, asked for once: a buffer never moves or resizes.
        void EnsureBuffer(int slot)
        {
            if (slots[slot].length < 0)
            {
                xlua_value* v = stackalloc xlua_value[4];
                v[0].i = slots[slot].nativeRef;
                Op(XLUA_HANDLE_BUFFER, v);
                slots[slot].data = v[2].s;
                slots[slot].length = v[2].len;
            }
        }

        internal IntPtr BufferPointer(int slot)
        {
            EnsureBuffer(slot);
            return slots[slot].data;
        }

        internal unsafe byte* BufferData(int slot, int generation, out int length)
        {
            NativeHandle(slot, generation, LuaType.Buffer);  // valid, and the owner thread
            EnsureBuffer(slot);
            length = slots[slot].length;
            return (byte*)slots[slot].data;
        }

        internal void ReleaseBuffer(int slot, int generation)
        {
            ReleaseHandle(slot, generation, LuaType.Buffer);
        }

        // Read<T> for LuaBufferRef: a buffer result is adopted as an owning handle; nil is default(LuaBufferRef).
        internal LuaBufferRef ReadBufferResult(ref xlua_value v)
        {
            if (v.type == XLUA_T_BUFFER)
            {
                int slot = Adopt(ref v);
                return new LuaBufferRef(this, slot, slots[slot].generation);
            }
            return ReadNoHandle<LuaBufferRef>(ref v, LuaType.Buffer);
        }
    }
}
#endif
