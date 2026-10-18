// SPIKE (spike/descriptor-wrappers, not a product feature; owner Q89,
// 2026-10-19): the erased core of descriptor ("ultra") wrappers, measured in
// docs/research/descriptor-wrappers.md. Compiled only with GEN_CODE_MINIMIZE
// and XLUA_SPIKE_DESC; it reuses GCM's trampoline (xlua_push_csharp_wrapper,
// the CSharpWrapper table and CSharpWrapperCallerImpl's exception handling).
//
// Per wrapped member the source generator emits only a descriptor (a few chars
// of one string per wrapped type) and one case of the type's switch, which
// performs the call on a DescArgs buffer. Everything a plain or GCM wrapper
// repeats in each member body (overload selection by count and kind, the Lua
// 5.3 integer rule, reading arguments, pushing the result, the error text)
// is here, once.
//
// Covered: methods, static methods, constructors, fields and properties (get
// and set), instance members of structs (copy, call, Update), parameters and
// results of primitive, string, class, interface and non-GCOptimize struct or
// enum types, overloads and optional parameters. Not covered (the generator
// leaves such members out in every spike mode): out/ref/in, params, events,
// indexers, operators, generic and extension methods, delegates, byte[],
// IntPtr, decimal, Nullable, LuaTable/LuaFunction and GCOptimize types.
#if GEN_CODE_MINIMIZE && XLUA_SPIKE_DESC
using System;
using LuaAPI = XLua.LuaDLL.Bridge;
using RealStatePtr = System.IntPtr;

namespace XLua
{
    // The argument buffer: argument i is in li, di or oi by its kind.
    public struct DescArgs
    {
        public object self;
        public long l0, l1, l2, l3, l4, l5, l6, l7;
        public double d0, d1, d2, d3, d4, d5, d6, d7;
        public object o0, o1, o2, o3, o4, o5, o6, o7;
        public long rl;
        public double rd;
        public object ro;
    }

    // One per wrapped type: a switch over the type's member cases.
    public delegate void DescInvoker(int id, ref DescArgs a);

    public sealed class DescType
    {
        // Kinds (SpikeDescriptors.cs writes the same numbers).
        internal const int K_VOID = 0, K_BOOL = 1, K_I8 = 2, K_U8 = 3, K_I16 = 4, K_U16 = 5, K_CHAR = 6, K_I32 = 7, K_U32 = 8,
            K_I64 = 9, K_U64 = 10, K_F32 = 11, K_F64 = 12, K_STR = 13, K_OBJ = 14, K_UOBJ = 15, K_ANY = 16, K_STRUCT = 17,
            K_RINT = 18, K_RPUSHANY = 19;
        internal const int F_STATIC = 1, F_CTOR = 2, F_STRUCT_SELF = 4, F_UPDATE = 8;

        internal readonly ObjectTranslator translator;
        internal readonly Type type;
        internal readonly string typeName;
        internal readonly Type[] types;
        internal readonly DescInvoker invoker;
#if !XLUA_SPIKE_DESC_STACK
        internal readonly DescArgs[] buffer = new DescArgs[1];
#endif
        readonly DescGroup[] groups;
        readonly int[] slots;

        static readonly int[] slotIndex = { Utils.METHOD_IDX, Utils.GETTER_IDX, Utils.SETTER_IDX, Utils.CLS_IDX, Utils.CLS_GETTER_IDX, Utils.CLS_SETTER_IDX };

        public DescType(ObjectTranslator translator, Type type, string typeName, string desc, Type[] types, DescInvoker invoker)
        {
            this.translator = translator;
            this.type = type;
            this.typeName = typeName;
            this.types = types;
            this.invoker = invoker;
            var list = new System.Collections.Generic.List<DescGroup>();
            var slotList = new System.Collections.Generic.List<int>();
            int p = 0;
            while (p < desc.Length)
            {
                int slot = desc[p++];
                int flags = desc[p++];
                int end = desc.IndexOf('\0', p);
                string name = desc.Substring(p, end - p);
                p = end + 1;
                int n = desc[p++];
                var g = new DescGroup(this, name, flags, n);
                for (int i = 0; i < n; i++)
                {
                    var o = new DescOverload();
                    o.id = desc[p++];
                    int argc = desc[p++];
                    o.kinds = new byte[argc];
                    o.argTypes = new Type[argc];
                    for (int k = 0; k < argc; k++)
                    {
                        int kind = desc[p++];
                        o.kinds[k] = (byte)kind;
                        if (kind == K_OBJ || kind == K_UOBJ || kind == K_STRUCT) o.argTypes[k] = types[desc[p++]];
                    }
                    o.ret = desc[p++];
                    g.overloads[i] = o;
                }
                list.Add(g);
                slotList.Add(slot);
            }
            groups = list.ToArray();
            slots = slotList.ToArray();
        }

        // The object members (slots 0..2) or the class members (3..5), between
        // Utils.Begin*Register and End*Register.
        public void Register(RealStatePtr L, bool cls)
        {
            for (int i = 0; i < groups.Length; i++)
            {
                int s = slots[i];
                if (s == 6 || (s >= 3) != cls) continue;
                Utils.RegisterFunc(L, slotIndex[s], groups[i].name, groups[i].Call);
            }
        }

        // The class table's __call: the constructor group.
        public CSharpWrapper Creator
        {
            get
            {
                for (int i = 0; i < groups.Length; i++)
                {
                    if (slots[i] == 6) return groups[i].Call;
                }
                return new DescGroup(this, "", F_CTOR, 0).Call;
            }
        }
    }

    sealed class DescOverload
    {
        public int id;
        public byte[] kinds;
        public Type[] argTypes;
        public int ret;
    }

    sealed class DescGroup
    {
        readonly DescType owner;
        internal readonly string name;
        readonly int flags;
        internal readonly DescOverload[] overloads;
        readonly ObjectTranslator translator;
        readonly DescInvoker invoker;
        readonly int start;
        readonly bool single, readSelf, structSelf, update;

        public DescGroup(DescType owner, string name, int flags, int n)
        {
            this.owner = owner;
            this.name = name;
            this.flags = flags;
            overloads = new DescOverload[n];
            translator = owner.translator;
            invoker = owner.invoker;
            bool ctor = (flags & DescType.F_CTOR) != 0;
            start = (flags & DescType.F_STATIC) != 0 ? 1 : 2;
            single = n == 1 && !ctor;
            readSelf = start == 2 && !ctor;
            structSelf = (flags & DescType.F_STRUCT_SELF) != 0;
            update = (flags & DescType.F_UPDATE) != 0;
        }

        public int Call(RealStatePtr L, int top)
        {
            var translator = this.translator;
            int start = this.start;
            DescOverload o = null;
            if (single)
            {
                o = overloads[0];
            }
            else
            {
                bool ctor = (flags & DescType.F_CTOR) != 0;
                for (int i = 0; i < overloads.Length; i++)
                {
                    var c = overloads[i];
                    if (top == c.kinds.Length + start - 1 && Matches(translator, L, start, c))
                    {
                        o = c;
                        break;
                    }
                }
                if (o == null)
                {
                    if (ctor)
                    {
                        return LuaAPI.luaL_error(L, overloads.Length == 0 && !owner.type.IsValueType
                            ? owner.typeName + " does not have a constructor!"
                            : "invalid arguments to " + owner.typeName + " constructor!");
                    }
                    return LuaAPI.luaL_error(L, "invalid arguments to " + owner.typeName + "." + name + "!");
                }
            }
#if !XLUA_SPIKE_DESC_STACK
            // Reused: a call consumes its arguments before the C# member runs and
            // writes its result after it returns, so a nested call does not clash.
            ref DescArgs a = ref owner.buffer[0];
#else
            var a = new DescArgs();
#endif
            if (readSelf)
            {
                a.self = structSelf ? translator.GetObject(L, 1, owner.type) : translator.FastGetCSObj(L, 1);
            }
            var kinds = o.kinds;
            for (int i = 0; i < kinds.Length; i++)
            {
                int idx = start + i;
                switch (kinds[i])
                {
                    case DescType.K_I32: SetL(ref a, i, LuaAPI.xlua_checkint32(L, idx)); break;
                    case DescType.K_F32: case DescType.K_F64: SetD(ref a, i, LuaAPI.lua_tonumber(L, idx)); break;
                    case DescType.K_STR: SetO(ref a, i, LuaAPI.lua_tostring(L, idx)); break;
                    case DescType.K_OBJ: SetO(ref a, i, translator.GetObject(L, idx, o.argTypes[i])); break;
                    default: Read(translator, L, idx, kinds[i], o.argTypes[i], ref a, i); break;
                }
            }
            invoker(o.id, ref a);
            int n;
            switch (o.ret)
            {
                case DescType.K_VOID: n = 0; break;
                case DescType.K_RINT: LuaAPI.xlua_pushinteger(L, (int)a.rl); n = 1; break;
                case DescType.K_F64: LuaAPI.lua_pushnumber(L, a.rd); n = 1; break;
                case DescType.K_STR: LuaAPI.lua_pushstring(L, (string)a.ro); n = 1; break;
                default: n = Push(translator, L, o.ret, ref a); break;
            }
            if (update && a.self != null) translator.Update(L, 1, a.self);
            return n;
        }

        static bool Matches(ObjectTranslator translator, RealStatePtr L, int start, DescOverload o)
        {
            var kinds = o.kinds;
            for (int i = 0; i < kinds.Length; i++)
            {
                int idx = start + i;
                bool ok;
                switch (kinds[i])
                {
                    case DescType.K_BOOL: ok = LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TBOOLEAN; break;
                    case DescType.K_I8: case DescType.K_U8: case DescType.K_I16: case DescType.K_U16: case DescType.K_CHAR:
                    case DescType.K_I32: case DescType.K_U32:
                        ok = LuaAPI.xlua_isinteger_number(L, idx); break;
                    case DescType.K_I64: ok = LuaAPI.xlua_isinteger_number(L, idx) || LuaAPI.lua_isint64(L, idx); break;
                    case DescType.K_U64: ok = LuaAPI.xlua_isinteger_number(L, idx) || LuaAPI.lua_isuint64(L, idx); break;
                    case DescType.K_F32: case DescType.K_F64: ok = LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TNUMBER; break;
                    case DescType.K_STR: ok = LuaAPI.lua_isnil(L, idx) || LuaAPI.lua_type(L, idx) == LuaTypes.LUA_TSTRING; break;
                    case DescType.K_ANY: ok = translator.Assignable(L, idx, typeof(object)); break;
                    default: ok = translator.Assignable(L, idx, o.argTypes[i]); break;
                }
                if (!ok) return false;
            }
            return true;
        }

        static void Read(ObjectTranslator translator, RealStatePtr L, int idx, int kind, Type t, ref DescArgs a, int i)
        {
            switch (kind)
            {
                case DescType.K_BOOL: SetL(ref a, i, LuaAPI.lua_toboolean(L, idx) ? 1 : 0); return;
                case DescType.K_I8: SetL(ref a, i, LuaAPI.xlua_checksbyte(L, idx)); return;
                case DescType.K_U8: SetL(ref a, i, LuaAPI.xlua_checkbyte(L, idx)); return;
                case DescType.K_I16: SetL(ref a, i, LuaAPI.xlua_checkint16(L, idx)); return;
                case DescType.K_U16: SetL(ref a, i, LuaAPI.xlua_checkuint16(L, idx)); return;
                case DescType.K_CHAR: SetL(ref a, i, LuaAPI.xlua_checkchar(L, idx)); return;
                case DescType.K_I32: SetL(ref a, i, LuaAPI.xlua_checkint32(L, idx)); return;
                case DescType.K_U32: SetL(ref a, i, LuaAPI.xlua_checkuint32(L, idx)); return;
                case DescType.K_I64: SetL(ref a, i, LuaAPI.xlua_checkinteger(L, idx)); return;
                case DescType.K_U64: SetL(ref a, i, (long)LuaAPI.xlua_checkuint64(L, idx)); return;
                case DescType.K_F32: case DescType.K_F64: SetD(ref a, i, LuaAPI.lua_tonumber(L, idx)); return;
                case DescType.K_STR: SetO(ref a, i, LuaAPI.lua_tostring(L, idx)); return;
                case DescType.K_UOBJ: SetO(ref a, i, translator.GetUnityObjectArg(L, idx, t)); return;
                case DescType.K_ANY: SetO(ref a, i, translator.GetObject(L, idx, typeof(object))); return;
                default: SetO(ref a, i, translator.GetObject(L, idx, t)); return;
            }
        }

        static int Push(ObjectTranslator translator, RealStatePtr L, int kind, ref DescArgs a)
        {
            switch (kind)
            {
                case DescType.K_VOID: return 0;
                case DescType.K_BOOL: LuaAPI.lua_pushboolean(L, a.rl != 0); return 1;
                case DescType.K_RINT: LuaAPI.xlua_pushinteger(L, (int)a.rl); return 1;
                case DescType.K_U32: LuaAPI.xlua_pushuint(L, (uint)a.rl); return 1;
                case DescType.K_I64: LuaAPI.lua_pushint64(L, a.rl); return 1;
                case DescType.K_U64: LuaAPI.lua_pushuint64(L, (ulong)a.rl); return 1;
                case DescType.K_F64: LuaAPI.lua_pushnumber(L, a.rd); return 1;
                case DescType.K_STR: LuaAPI.lua_pushstring(L, (string)a.ro); return 1;
                case DescType.K_RPUSHANY: translator.PushAny(L, a.ro); return 1;
                default: translator.Push(L, a.ro); return 1;
            }
        }

        static void SetL(ref DescArgs a, int i, long v)
        {
            switch (i)
            {
                case 0: a.l0 = v; break; case 1: a.l1 = v; break; case 2: a.l2 = v; break; case 3: a.l3 = v; break;
                case 4: a.l4 = v; break; case 5: a.l5 = v; break; case 6: a.l6 = v; break; default: a.l7 = v; break;
            }
        }

        static void SetD(ref DescArgs a, int i, double v)
        {
            switch (i)
            {
                case 0: a.d0 = v; break; case 1: a.d1 = v; break; case 2: a.d2 = v; break; case 3: a.d3 = v; break;
                case 4: a.d4 = v; break; case 5: a.d5 = v; break; case 6: a.d6 = v; break; default: a.d7 = v; break;
            }
        }

        static void SetO(ref DescArgs a, int i, object v)
        {
            switch (i)
            {
                case 0: a.o0 = v; break; case 1: a.o1 = v; break; case 2: a.o2 = v; break; case 3: a.o3 = v; break;
                case 4: a.o4 = v; break; case 5: a.o5 = v; break; case 6: a.o6 = v; break; default: a.o7 = v; break;
            }
        }
    }
}
#endif
