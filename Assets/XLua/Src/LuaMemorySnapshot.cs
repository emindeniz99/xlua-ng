/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// Lua memory diagnostics for development builds: LuaEnv.TakeMemorySnapshot()
// and LuaMemoryDiagnostics.Diff(). A snapshot lists the Lua objects reachable
// from the roots (tables, functions, userdata, threads) with their sizes and the
// path that reaches each one, the C# objects Lua holds (ObjectTranslator's
// object pool) and the registry references C# holds into Lua (LuaTable,
// LuaFunction, delegate bridges). The walk is xlua_memory_snapshot in
// build/memory_leak_checker.c. The Editor window is XLua/Lua Memory
// (Editor/LuaMemoryWindow.cs), the Lua module is xlua.memory (EmbeddedModules.cs).
//
// Compiled in the Editor and in Development Builds, and elsewhere only with
// XLUA_MEMORY_DIAGNOSTICS, so release players carry none of it.

#if UNITY_EDITOR || DEVELOPMENT_BUILD || XLUA_MEMORY_DIAGNOSTICS

#if USE_UNI_LUA
using LuaAPI = UniLua.Lua;
using RealStatePtr = UniLua.ILuaState;
#else
using LuaAPI = XLua.LuaDLL.Bridge;
using RealStatePtr = System.IntPtr;
#endif

namespace XLua
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Text;

    // The Lua type of an object in a snapshot (the LUA_T* value).
    public enum LuaMemoryObjectType
    {
        Table = 5,
        Function = 6,
        Userdata = 7,
        Thread = 8,
    }

    [Flags]
    public enum LuaMemoryObjectFlags
    {
        None = 0,
        WeakKeys = 1,
        WeakValues = 2,
        HasMetatable = 4,
        // The metatable has __gc: a finalizer runs when the object is collected.
        HasFinalizer = 8,
        CFunction = 16,
        // xLua userdata for a C# object: CSharpObjectIndex is its ObjectPool index.
        CSharpObject = 32,
        // xLua userdata holding a copy of a C# struct.
        CSharpStruct = 64,
        // The thread the snapshot was taken on (LuaEnv's main thread).
        CurrentThread = 128,
    }

    // How a reference holds its target.
    public enum LuaMemoryReferenceKind
    {
        Root = 1,
        TableValue = 2,
        TableKey = 3,
        Metatable = 4,
        Upvalue = 5,
        UserValue = 6,
        // A function or userdata environment (Lua 5.1 and LuaJIT).
        Environment = 7,
        // A stack slot of a thread that is not running.
        StackSlot = 8,
        // A local variable or temporary of a call frame.
        Local = 9,
        // The function of a call frame.
        Frame = 10,
    }

    public sealed class LuaMemorySnapshotOptions
    {
        // Run a full Lua collection first, so that the snapshot holds only live objects.
        public bool FullGc = true;
        // Also run the C# collector, wait for finalizers and call LuaEnv.Tick first, so that
        // LuaTable, LuaFunction and delegates no longer referenced from C# release their Lua side.
        public bool CollectCSharpGarbage = false;
        // Stop recording new Lua objects after this many (0: no limit); LuaMemorySnapshot.Truncated tells.
        public int MaxObjects = 0;
        // Call ToString (or read UnityEngine.Object.name) for each C# object Lua holds.
        public bool DescribeCSharpObjects = true;
    }

    // A table, function, userdata or thread reachable from the roots.
    public sealed class LuaMemoryObject
    {
        internal LuaMemorySnapshot snapshot;
        internal int index;
        internal int parentEdge;

        public ulong Address { get; internal set; }
        public LuaMemoryObjectType Type { get; internal set; }
        public LuaMemoryObjectFlags Flags { get; internal set; }
        // Table entries, upvalues, user values or stack slots.
        public int Count { get; internal set; }
        // Bytes: tables and closures on Lua 5.3 to 5.5, userdata blocks everywhere; -1 when unknown.
        public long Size { get; internal set; }
        // "file.lua:12" where a Lua function is defined; a thread's status.
        public string Source { get; internal set; }
        // ObjectPool index of the C# object for CSharpObject userdata, otherwise -1.
        public int CSharpObjectIndex { get; internal set; }
        // The C# object this userdata stands for, when it is still in the pool.
        public LuaMemoryCSharpObject CSharpObject { get; internal set; }

        public LuaMemorySnapshot Snapshot { get { return snapshot; } }

        // The reference the walk reached this object through first: the last step of a shortest path.
        public LuaMemoryReference FirstReference { get { return snapshot.Reference(parentEdge); } }

        // A shortest path from a root, such as "_G.ui.panels[3].button".
        public string Path { get { return snapshot.PathOf(this); } }

        // Every reference to this object.
        public IList<LuaMemoryReference> ReferencedBy { get { return snapshot.ReferencesTo(this); } }

        public bool Is(LuaMemoryObjectFlags flag)
        {
            return (Flags & flag) != 0;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(LuaMemorySnapshot.TypeName(Type)).Append(" 0x").Append(Address.ToString("x", CultureInfo.InvariantCulture));
            if (CSharpObject != null)
            {
                sb.Append(" (").Append(CSharpObject.TypeName).Append(')');
            }
            else if (Source != null)
            {
                sb.Append(" (").Append(Source).Append(')');
            }
            return sb.ToString();
        }
    }

    // A reference from one object (or a root) to another.
    public sealed class LuaMemoryReference
    {
        // null for a root.
        public LuaMemoryObject From { get; internal set; }
        public LuaMemoryObject To { get; internal set; }
        public LuaMemoryReferenceKind Kind { get; internal set; }
        // The key as text: a table key ("name", "3", "true", "table: 0x..."), an upvalue or local
        // name, a root name ("_G", "registry", "thread", "metatable(string)") or a frame.
        public string Key { get; internal set; }
        // The table key when it is an integer, such as a registry reference.
        public long? IntegerKey { get; internal set; }

        // How this step reads in a path: ".name", "[3]", ".(metatable)" and so on.
        public string PathSegment
        {
            get
            {
                switch (Kind)
                {
                    case LuaMemoryReferenceKind.Root: return Key;
                    case LuaMemoryReferenceKind.TableValue:
                        if (!keyIsString) return "[" + Key + "]";
                        return IsIdentifier(Key) ? "." + Key : "[\"" + Key.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"]";
                    case LuaMemoryReferenceKind.TableKey: return ".(key)";
                    case LuaMemoryReferenceKind.Metatable: return ".(metatable)";
                    case LuaMemoryReferenceKind.Upvalue: return ".(upvalue " + Key + ")";
                    case LuaMemoryReferenceKind.UserValue: return ".(uservalue " + Key + ")";
                    case LuaMemoryReferenceKind.Environment: return ".(environment)";
                    case LuaMemoryReferenceKind.StackSlot: return ".(stack " + Key + ")";
                    case LuaMemoryReferenceKind.Local: return ".(local " + Key + ")";
                    case LuaMemoryReferenceKind.Frame: return ".(frame " + Key + ")";
                    default: return ".(" + Key + ")";
                }
            }
        }

        internal bool keyIsString;

        static readonly HashSet<string> keywords = new HashSet<string> {
            "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "global", "goto", "if", "in",
            "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while" };

        static bool IsIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s) || keywords.Contains(s) || char.IsDigit(s[0])) return false;
            foreach (char c in s)
            {
                if (!(c == '_' || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))) return false;
            }
            return true;
        }

        public override string ToString()
        {
            return (From == null ? "" : From.ToString() + " ") + PathSegment + " -> " + To;
        }
    }

    // A C# object in ObjectTranslator's pool, that is, held by a Lua userdata.
    public sealed class LuaMemoryCSharpObject
    {
        internal WeakReference target;

        // The ObjectPool index stored in the userdata.
        public int Index { get; internal set; }
        public Type Type { get; internal set; }
        public string TypeName { get { return Type == null ? "null" : Type.FullName; } }
        // ToString(), or the name of a UnityEngine.Object; null when not described.
        public string Description { get; internal set; }
        // A UnityEngine.Object that was destroyed while Lua still holds it.
        public bool IsDestroyed { get; internal set; }
        // The userdata that holds it, or null when no reachable userdata does (it waits for __gc).
        public LuaMemoryObject LuaObject { get; internal set; }
        // The object itself, while it is alive; the snapshot does not keep it alive.
        public object Target { get { return target == null ? null : target.Target; } }

        public string Path { get { return LuaObject == null ? null : LuaObject.Path; } }

        public override string ToString()
        {
            var sb = new StringBuilder(TypeName);
            if (IsDestroyed) sb.Append(" (destroyed)");
            else if (Description != null) sb.Append(" \"").Append(Description).Append('"');
            sb.Append(' ').Append(Path ?? "(not reachable from Lua; released at its next __gc)");
            return sb.ToString();
        }
    }

    // An integer key of the registry: a luaL_ref that C# (LuaTable, LuaFunction, a delegate
    // bridge) or xLua itself holds.
    public sealed class LuaMemoryRegistryReference
    {
        public int Reference { get; internal set; }
        public LuaMemoryObject Object { get; internal set; }
        // "delegate bridge", "type metatable", "xLua object cache", "xLua error handler",
        // "Lua main thread", "Lua globals", or null for a LuaTable, LuaFunction or other ref.
        public string Holder { get; internal set; }

        public override string ToString()
        {
            return "registry[" + Reference + "] " + Object + (Holder == null ? "" : " (" + Holder + ")");
        }
    }

    public sealed class LuaMemoryTypeStats
    {
        public int Count { get; internal set; }
        // Sum of the known sizes in bytes.
        public long Size { get; internal set; }
    }

    // A table present in both snapshots that has more entries in the second.
    public sealed class LuaMemoryTableGrowth
    {
        public LuaMemoryObject Before { get; internal set; }
        public LuaMemoryObject After { get; internal set; }
        public int CountDelta { get { return After.Count - Before.Count; } }
        public long SizeDelta { get { return After.Size - Before.Size; } }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "+{0} entries ({1} -> {2}) {3}",
                CountDelta, Before.Count, After.Count, After.Path);
        }
    }

    public sealed class LuaMemorySnapshot
    {
        // Native layout (build/memory_leak_checker.c): header, nodes, edges, string pool.
        const int Magic = 0x534D4C58, Version = 1, HeaderBytes = 64, NodeBytes = 40, EdgeBytes = 32;
        const int KeyString = 1, KeyInteger = 2, KeyNumber = 3, KeyBoolean = 4, KeyLightUserdata = 5, KeyObject = 16;

        readonly int[] edgeFrom, edgeTo, edgeKind, edgeKeyType;
        readonly long[] edgeKey;
        readonly double[] edgeNumber;
        readonly byte[] pool;
        readonly Dictionary<int, string> strings = new Dictionary<int, string>();
        readonly Dictionary<ulong, LuaMemoryObject> byAddress = new Dictionary<ulong, LuaMemoryObject>();
        int[] incomingStart, incoming;  // edges by target, built on first use

        public DateTime Time { get; private set; }
        // Lua heap size (collectgarbage("count")) when the walk started.
        public long LuaHeapBytes { get; private set; }
        // MaxObjects was reached: objects further from the roots are missing.
        public bool Truncated { get; private set; }
        // LUA_VERSION_NUM of the plugin: 503, 504, 505, or 501 for LuaJIT.
        public int LuaVersion { get; private set; }
        // Distinct strings reachable from the roots, and their bytes. Strings are not objects here.
        public int StringCount { get; private set; }
        public long StringBytes { get; private set; }

        public IList<LuaMemoryObject> Objects { get; private set; }
        // Every C# object in the pool, held by Lua userdata.
        public IList<LuaMemoryCSharpObject> CSharpObjects { get; private set; }
        // Lua objects that C# or xLua hold through the registry.
        public IList<LuaMemoryRegistryReference> RegistryReferences { get; private set; }
        public int ReferenceCount { get { return edgeFrom.Length; } }

        internal LuaMemorySnapshot(byte[] data, DateTime time)
        {
            Time = time;
            if (data.Length < HeaderBytes || BitConverter.ToInt32(data, 0) != Magic || BitConverter.ToInt32(data, 4) != Version
                || BitConverter.ToInt32(data, 8) != HeaderBytes || BitConverter.ToInt32(data, 12) != NodeBytes
                || BitConverter.ToInt32(data, 16) != EdgeBytes)
            {
                throw new InvalidOperationException("the native plugin wrote a memory snapshot of another format; rebuild it from this xLua");
            }
            int nodeCount = BitConverter.ToInt32(data, 20), edgeCount = BitConverter.ToInt32(data, 24);
            int stringBytes = BitConverter.ToInt32(data, 28), flags = BitConverter.ToInt32(data, 32);
            LuaVersion = BitConverter.ToInt32(data, 36);
            LuaHeapBytes = BitConverter.ToInt64(data, 40);
            StringBytes = BitConverter.ToInt64(data, 48);
            StringCount = BitConverter.ToInt32(data, 56);
            Truncated = (flags & 1) != 0;

            var objects = new LuaMemoryObject[nodeCount];
            int offset = HeaderBytes;
            int[] info = new int[nodeCount];
            for (int i = 0; i < nodeCount; i++, offset += NodeBytes)
            {
                var o = new LuaMemoryObject();
                o.snapshot = this;
                o.index = i;
                o.Address = BitConverter.ToUInt64(data, offset);
                o.Size = BitConverter.ToInt64(data, offset + 8);
                o.Type = (LuaMemoryObjectType)BitConverter.ToInt32(data, offset + 16);
                o.Flags = (LuaMemoryObjectFlags)BitConverter.ToInt32(data, offset + 20);
                o.Count = BitConverter.ToInt32(data, offset + 24);
                o.CSharpObjectIndex = BitConverter.ToInt32(data, offset + 28);
                info[i] = BitConverter.ToInt32(data, offset + 32);
                o.parentEdge = BitConverter.ToInt32(data, offset + 36);
                objects[i] = o;
                byAddress[o.Address] = o;
            }
            edgeFrom = new int[edgeCount];
            edgeTo = new int[edgeCount];
            edgeKind = new int[edgeCount];
            edgeKeyType = new int[edgeCount];
            edgeKey = new long[edgeCount];
            edgeNumber = new double[edgeCount];
            for (int i = 0; i < edgeCount; i++, offset += EdgeBytes)
            {
                edgeFrom[i] = BitConverter.ToInt32(data, offset);
                edgeTo[i] = BitConverter.ToInt32(data, offset + 4);
                edgeKind[i] = BitConverter.ToInt32(data, offset + 8);
                edgeKeyType[i] = BitConverter.ToInt32(data, offset + 12);
                edgeKey[i] = BitConverter.ToInt64(data, offset + 16);
                edgeNumber[i] = BitConverter.ToDouble(data, offset + 24);
            }
            pool = new byte[stringBytes];
            Buffer.BlockCopy(data, offset, pool, 0, stringBytes);
            for (int i = 0; i < nodeCount; i++)
            {
                if (info[i] >= 0) objects[i].Source = PoolString(info[i]);
            }
            Objects = objects;
            CSharpObjects = new LuaMemoryCSharpObject[0];
            RegistryReferences = new LuaMemoryRegistryReference[0];
        }

        string PoolString(long offset)
        {
            string s;
            int at = (int)offset;
            if (!strings.TryGetValue(at, out s))
            {
                int length = BitConverter.ToInt32(pool, at);
                s = Encoding.UTF8.GetString(pool, at + 4, length);
                strings[at] = s;
            }
            return s;
        }

        internal static string TypeName(LuaMemoryObjectType type)
        {
            switch (type)
            {
                case LuaMemoryObjectType.Table: return "table";
                case LuaMemoryObjectType.Function: return "function";
                case LuaMemoryObjectType.Userdata: return "userdata";
                case LuaMemoryObjectType.Thread: return "thread";
                default: return "type " + (int)type;
            }
        }

        string KeyText(int edge)
        {
            int type = edgeKeyType[edge];
            switch (type)
            {
                case 0: return "";
                case KeyString: return PoolString(edgeKey[edge]);
                case KeyInteger: return edgeKey[edge].ToString(CultureInfo.InvariantCulture);
                case KeyNumber: return edgeNumber[edge].ToString("R", CultureInfo.InvariantCulture);
                case KeyBoolean: return edgeKey[edge] != 0 ? "true" : "false";
                case KeyLightUserdata: return "lightuserdata: 0x" + edgeKey[edge].ToString("x", CultureInfo.InvariantCulture);
                default:
                    return (type >= KeyObject ? TypeName((LuaMemoryObjectType)(type - KeyObject)) : "?")
                        + ": 0x" + edgeKey[edge].ToString("x", CultureInfo.InvariantCulture);
            }
        }

        public LuaMemoryReference Reference(int edge)
        {
            if (edge < 0 || edge >= edgeFrom.Length) return null;
            var r = new LuaMemoryReference();
            r.From = edgeFrom[edge] < 0 ? null : Objects[edgeFrom[edge]];
            r.To = Objects[edgeTo[edge]];
            r.Kind = (LuaMemoryReferenceKind)edgeKind[edge];
            r.Key = KeyText(edge);
            r.keyIsString = edgeKeyType[edge] == KeyString;
            if (edgeKeyType[edge] == KeyInteger) r.IntegerKey = edgeKey[edge];
            return r;
        }

        public LuaMemoryObject Find(ulong address)
        {
            LuaMemoryObject o;
            return byAddress.TryGetValue(address, out o) ? o : null;
        }

        // The entry for a C# object that Lua holds, or null.
        public LuaMemoryCSharpObject FindCSharpObject(object obj)
        {
            foreach (var entry in CSharpObjects)
            {
                if (ReferenceEquals(entry.Target, obj)) return entry;
            }
            return null;
        }

        void BuildIncoming()
        {
            if (incoming != null) return;
            int n = Objects.Count;
            var start = new int[n + 1];
            foreach (int to in edgeTo) start[to + 1]++;
            for (int i = 0; i < n; i++) start[i + 1] += start[i];
            var fill = (int[])start.Clone();
            var list = new int[edgeTo.Length];
            for (int e = 0; e < edgeTo.Length; e++) list[fill[edgeTo[e]]++] = e;
            incomingStart = start;
            incoming = list;
        }

        public IList<LuaMemoryReference> ReferencesTo(LuaMemoryObject o)
        {
            BuildIncoming();
            var result = new List<LuaMemoryReference>();
            for (int i = incomingStart[o.index]; i < incomingStart[o.index + 1]; i++)
            {
                result.Add(Reference(incoming[i]));
            }
            return result;
        }

        // Where a reference comes from and how, to recognise the same object in another snapshot.
        internal IEnumerable<string> ReferrerSignatures(LuaMemoryObject o)
        {
            BuildIncoming();
            for (int i = incomingStart[o.index]; i < incomingStart[o.index + 1]; i++)
            {
                int e = incoming[i];
                ulong from = edgeFrom[e] < 0 ? 0 : Objects[edgeFrom[e]].Address;
                yield return from.ToString("x", CultureInfo.InvariantCulture) + "/" + edgeKind[e] + "/" + KeyText(e);
            }
        }

        public string PathOf(LuaMemoryObject o)
        {
            const int maxSteps = 64;
            var segments = new List<string>();
            int node = o.index, steps = 0;
            while (node >= 0)
            {
                int e = Objects[node].parentEdge;
                if (e < 0) break;
                if (++steps > maxSteps)
                {
                    int rest = 0;
                    for (int n = node; n >= 0 && Objects[n].parentEdge >= 0; n = edgeFrom[Objects[n].parentEdge]) rest++;
                    segments.Add("(" + rest + " more steps)");
                    break;
                }
                segments.Add(Reference(e).PathSegment);
                node = edgeFrom[e];
            }
            segments.Reverse();
            return string.Concat(segments.ToArray());
        }

        public LuaMemoryTypeStats Stats(LuaMemoryObjectType type)
        {
            var stats = new LuaMemoryTypeStats();
            foreach (var o in Objects)
            {
                if (o.Type != type) continue;
                stats.Count++;
                if (o.Size > 0) stats.Size += o.Size;
            }
            return stats;
        }

        // C# objects held by Lua, counted by type name.
        public IDictionary<string, int> CSharpCountsByType()
        {
            var counts = new Dictionary<string, int>();
            foreach (var c in CSharpObjects)
            {
                int n;
                counts.TryGetValue(c.TypeName, out n);
                counts[c.TypeName] = n + 1;
            }
            return counts;
        }

        internal void Attach(LuaEnv env, LuaMemorySnapshotOptions options)
        {
            ObjectTranslator translator = env.translator;
            var csobjects = new List<LuaMemoryCSharpObject>();
            var byIndex = new Dictionary<int, LuaMemoryCSharpObject>();
            ObjectPool pool = translator.objects;
            for (int i = 0; i < pool.HighWater; i++)
            {
                object obj;
                if (!pool.TryGetValue(i, out obj) || obj == null) continue;
                var entry = new LuaMemoryCSharpObject();
                entry.Index = i;
                entry.Type = obj.GetType();
                entry.target = new WeakReference(obj);
                Describe(entry, obj, options.DescribeCSharpObjects);
                csobjects.Add(entry);
                byIndex[i] = entry;
            }
            foreach (var o in Objects)
            {
                LuaMemoryCSharpObject entry;
                if (o.CSharpObjectIndex >= 0 && byIndex.TryGetValue(o.CSharpObjectIndex, out entry))
                {
                    o.CSharpObject = entry;
                    entry.LuaObject = o;
                }
            }
            CSharpObjects = csobjects;
            RegistryReferences = FindRegistryReferences(env);
        }

        static void Describe(LuaMemoryCSharpObject entry, object obj, bool describe)
        {
#if !XLUA_GENERAL
            var unityObject = obj as UnityEngine.Object;
            if (!ReferenceEquals(unityObject, null))
            {
                entry.IsDestroyed = unityObject == null;
                if (describe && !entry.IsDestroyed) entry.Description = unityObject.name;
                return;
            }
#endif
            if (!describe) return;
            try
            {
                string text = obj.ToString();
                entry.Description = text != null && text.Length > 120 ? text.Substring(0, 120) + "..." : text;
            }
            catch (Exception e)
            {
                entry.Description = "(ToString threw " + e.GetType().Name + ")";
            }
        }

        IList<LuaMemoryRegistryReference> FindRegistryReferences(LuaEnv env)
        {
            var result = new List<LuaMemoryRegistryReference>();
            LuaMemoryObject registry = null;
            for (int e = 0; e < edgeFrom.Length && registry == null; e++)
            {
                if (edgeKind[e] == (int)LuaMemoryReferenceKind.Root && KeyText(e) == "registry") registry = Objects[edgeTo[e]];
            }
            if (registry == null) return result;
            var bridges = DelegateBridgeRefs(env.translator);
            for (int e = 0; e < edgeFrom.Length; e++)
            {
                if (edgeFrom[e] != registry.index || edgeKind[e] != (int)LuaMemoryReferenceKind.TableValue
                    || edgeKeyType[e] != KeyInteger) continue;
                var r = new LuaMemoryRegistryReference();
                r.Reference = (int)edgeKey[e];
                r.Object = Objects[edgeTo[e]];
                // LUA_RIDX_MAINTHREAD is 1 up to Lua 5.4 and 3 in 5.5; LUA_RIDX_GLOBALS is 2.
                if (LuaVersion >= 502 && r.Reference <= 3 && r.Object.Type == LuaMemoryObjectType.Thread) r.Holder = "Lua main thread";
                else if (LuaVersion >= 502 && r.Reference == 2) r.Holder = "Lua globals";
                else if (r.Reference == env.translator.cacheRef) r.Holder = "xLua object cache";
                else if (r.Reference == env.errorFuncRef) r.Holder = "xLua error handler";
                else if (bridges.Contains(r.Reference)) r.Holder = "delegate bridge";
                else if (IsMetatable(r.Object)) r.Holder = "type metatable";
                result.Add(r);
            }
            return result;
        }

        bool IsMetatable(LuaMemoryObject o)
        {
            BuildIncoming();
            for (int i = incomingStart[o.index]; i < incomingStart[o.index + 1]; i++)
            {
                if (edgeKind[incoming[i]] == (int)LuaMemoryReferenceKind.Metatable) return true;
            }
            return false;
        }

        // The registry references of the live delegate bridges. ObjectTranslator keeps them in a
        // private field; reading it by reflection keeps this diagnostic out of ObjectTranslator.
        static HashSet<int> DelegateBridgeRefs(ObjectTranslator translator)
        {
            var refs = new HashSet<int>();
            try
            {
                FieldInfo field = typeof(ObjectTranslator).GetField("delegate_bridges", BindingFlags.Instance | BindingFlags.NonPublic);
                var bridges = field == null ? null : field.GetValue(translator) as Dictionary<int, WeakReference>;
                if (bridges == null) return refs;
                foreach (var kv in bridges)
                {
                    if (kv.Value != null && kv.Value.IsAlive) refs.Add(kv.Key);
                }
            }
            catch (Exception)
            {
                // Labels only; a stripped field leaves the references unlabelled.
            }
            return refs;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Lua memory snapshot {0:yyyy-MM-dd HH:mm:ss}: Lua heap {1} KB, {2} objects{3}, {4} C# objects held by Lua, {5} registry references",
                Time, LuaHeapBytes / 1024, Objects.Count, Truncated ? " (truncated)" : "", CSharpObjects.Count, RegistryReferences.Count);
        }

        // A text report: counts and sizes by type, the C# objects Lua holds by type, destroyed
        // Unity objects still held, and the largest tables with their paths.
        public string Report(int top = 20)
        {
            var sb = new StringBuilder();
            sb.AppendLine(ToString());
            AppendTypeStats(sb, this, null);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  strings    {0,8}  {1,10} B (distinct, reachable)", StringCount, StringBytes));
            AppendCounts(sb, "C# objects held by Lua, by type:", CSharpCountsByType(), null, top);
            var destroyed = new List<LuaMemoryCSharpObject>();
            foreach (var c in CSharpObjects) if (c.IsDestroyed) destroyed.Add(c);
            if (destroyed.Count > 0)
            {
                sb.AppendLine("Destroyed UnityEngine.Objects still held by Lua: " + destroyed.Count);
                for (int i = 0; i < destroyed.Count && i < top; i++) sb.Append("  ").AppendLine(destroyed[i].ToString());
            }
            var tables = new List<LuaMemoryObject>();
            foreach (var o in Objects) if (o.Type == LuaMemoryObjectType.Table) tables.Add(o);
            tables.Sort((a, b) => b.Size != a.Size ? b.Size.CompareTo(a.Size) : b.Count.CompareTo(a.Count));
            sb.AppendLine("Largest tables:");
            for (int i = 0; i < tables.Count && i < top; i++)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,10} B {1,8} entries  {2}",
                    tables[i].Size < 0 ? "?" : tables[i].Size.ToString(CultureInfo.InvariantCulture), tables[i].Count, tables[i].Path));
            }
            int held = 0;
            foreach (var r in RegistryReferences) if (r.Holder == null || r.Holder == "delegate bridge") held++;
            sb.AppendLine("Registry references (Lua objects held from C#): " + RegistryReferences.Count + ", of which "
                + held + " LuaTable, LuaFunction or delegate bridge references");
            return sb.ToString();
        }

        internal static void AppendTypeStats(StringBuilder sb, LuaMemorySnapshot after, LuaMemorySnapshot before)
        {
            foreach (LuaMemoryObjectType type in new[] { LuaMemoryObjectType.Table, LuaMemoryObjectType.Function,
                LuaMemoryObjectType.Userdata, LuaMemoryObjectType.Thread })
            {
                var a = after.Stats(type);
                if (before == null)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,-9}  {1,8}  {2,10} B", TypeName(type), a.Count, a.Size));
                }
                else
                {
                    var b = before.Stats(type);
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,-9}  {1,8} ({2:+#;-#;0})  {3,10} B ({4:+#;-#;0})",
                        TypeName(type), a.Count, a.Count - b.Count, a.Size, a.Size - b.Size));
                }
            }
        }

        internal static void AppendCounts(StringBuilder sb, string title, IDictionary<string, int> counts,
            IDictionary<string, int> before, int top)
        {
            var list = new List<KeyValuePair<string, int>>(counts);
            Func<string, int> delta = name =>
            {
                int b = 0;
                if (before != null) before.TryGetValue(name, out b);
                return counts.ContainsKey(name) ? counts[name] - b : -b;
            };
            if (before != null)
            {
                foreach (var kv in before) if (!counts.ContainsKey(kv.Key)) list.Add(new KeyValuePair<string, int>(kv.Key, 0));
                list.RemoveAll(kv => delta(kv.Key) == 0);
                list.Sort((x, y) => Math.Abs(delta(y.Key)).CompareTo(Math.Abs(delta(x.Key))));
            }
            else
            {
                list.Sort((x, y) => y.Value.CompareTo(x.Value));
            }
            sb.AppendLine(title);
            for (int i = 0; i < list.Count && i < top; i++)
            {
                if (before == null)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,8}  {1}", list[i].Value, list[i].Key));
                }
                else
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,8:+#;-#;0}  {1} (now {2})",
                        delta(list[i].Key), list[i].Key, list[i].Value));
                }
            }
        }
    }

    // What changed between two snapshots of the same LuaEnv.
    public sealed class LuaMemoryDiff
    {
        public LuaMemorySnapshot Before { get; private set; }
        public LuaMemorySnapshot After { get; private set; }
        public long HeapDelta { get { return After.LuaHeapBytes - Before.LuaHeapBytes; } }
        // Objects of After that Before did not have, nearest to a root first.
        public IList<LuaMemoryObject> Added { get; private set; }
        // Objects of Before that After does not have.
        public IList<LuaMemoryObject> Removed { get; private set; }
        // Tables in both whose entry count grew, the largest growth first.
        public IList<LuaMemoryTableGrowth> GrownTables { get; private set; }
        // C# objects Lua holds in After but did not hold in Before, and the reverse.
        public IList<LuaMemoryCSharpObject> AddedCSharpObjects { get; private set; }
        public IList<LuaMemoryCSharpObject> RemovedCSharpObjects { get; private set; }
        // Registry references new in After: Lua objects that C# started holding.
        public IList<LuaMemoryRegistryReference> AddedRegistryReferences { get; private set; }

        // An object counts as the same in both snapshots when it has the same address and type and
        // is still referenced the same way (same referrer address, kind and key) at least once.
        // Addresses alone are reused after a collection.
        internal LuaMemoryDiff(LuaMemorySnapshot before, LuaMemorySnapshot after)
        {
            Before = before;
            After = after;
            var added = new List<LuaMemoryObject>();
            var grown = new List<LuaMemoryTableGrowth>();
            var matched = new HashSet<LuaMemoryObject>();
            foreach (var o in after.Objects)
            {
                var b = before.Find(o.Address);
                if (b != null && b.Type == o.Type && b.Source == o.Source && SameReferrer(before, b, after, o))
                {
                    matched.Add(b);
                    if (o.Type == LuaMemoryObjectType.Table && o.Count > b.Count)
                    {
                        var g = new LuaMemoryTableGrowth();
                        g.Before = b;
                        g.After = o;
                        grown.Add(g);
                    }
                }
                else
                {
                    added.Add(o);
                }
            }
            var removed = new List<LuaMemoryObject>();
            foreach (var b in before.Objects) if (!matched.Contains(b)) removed.Add(b);
            grown.Sort((x, y) => y.CountDelta.CompareTo(x.CountDelta));
            Added = added;
            Removed = removed;
            GrownTables = grown;

            var beforeTargets = new Dictionary<object, LuaMemoryCSharpObject>(new ReferenceEqualsComparer());
            foreach (var c in before.CSharpObjects)
            {
                object t = c.Target;
                if (t != null) beforeTargets[t] = c;
            }
            var afterTargets = new Dictionary<object, LuaMemoryCSharpObject>(new ReferenceEqualsComparer());
            var addedCs = new List<LuaMemoryCSharpObject>();
            foreach (var c in after.CSharpObjects)
            {
                object t = c.Target;
                if (t == null) continue;
                afterTargets[t] = c;
                if (!beforeTargets.ContainsKey(t)) addedCs.Add(c);
            }
            var removedCs = new List<LuaMemoryCSharpObject>();
            foreach (var c in before.CSharpObjects)
            {
                object t = c.Target;
                if (t == null || !afterTargets.ContainsKey(t)) removedCs.Add(c);
            }
            AddedCSharpObjects = addedCs;
            RemovedCSharpObjects = removedCs;

            var beforeRefs = new HashSet<string>();
            foreach (var r in before.RegistryReferences) beforeRefs.Add(r.Reference + "/" + r.Object.Address);
            var addedRefs = new List<LuaMemoryRegistryReference>();
            foreach (var r in after.RegistryReferences)
            {
                if (!beforeRefs.Contains(r.Reference + "/" + r.Object.Address)) addedRefs.Add(r);
            }
            AddedRegistryReferences = addedRefs;
        }

        static bool SameReferrer(LuaMemorySnapshot before, LuaMemoryObject b, LuaMemorySnapshot after, LuaMemoryObject a)
        {
            var signatures = new HashSet<string>(before.ReferrerSignatures(b));
            foreach (var s in after.ReferrerSignatures(a))
            {
                if (signatures.Contains(s)) return true;
            }
            return false;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "Lua memory diff: heap {0:+#;-#;0} KB, objects +{1} -{2}, {3} grown tables, C# objects held by Lua +{4} -{5}, registry references +{6}",
                HeapDelta / 1024, Added.Count, Removed.Count, GrownTables.Count, AddedCSharpObjects.Count,
                RemovedCSharpObjects.Count, AddedRegistryReferences.Count);
        }

        // A text report of the changes, at most `top` lines per section.
        public string Report(int top = 20)
        {
            var sb = new StringBuilder();
            sb.AppendLine(ToString());
            LuaMemorySnapshot.AppendTypeStats(sb, After, Before);
            if (GrownTables.Count > 0)
            {
                sb.AppendLine("Grown tables:");
                for (int i = 0; i < GrownTables.Count && i < top; i++) sb.Append("  ").AppendLine(GrownTables[i].ToString());
            }
            if (Added.Count > 0)
            {
                sb.AppendLine("Added objects (nearest to a root first):");
                for (int i = 0; i < Added.Count && i < top; i++)
                {
                    sb.Append("  ").Append(Added[i]).Append("  ").AppendLine(Added[i].Path);
                }
            }
            if (AddedCSharpObjects.Count > 0 || RemovedCSharpObjects.Count > 0)
            {
                var afterCounts = new Dictionary<string, int>(After.CSharpCountsByType());
                LuaMemorySnapshot.AppendCounts(sb, "C# objects held by Lua, change by type:", afterCounts,
                    Before.CSharpCountsByType(), top);
            }
            if (AddedCSharpObjects.Count > 0)
            {
                sb.AppendLine("C# objects Lua started holding:");
                for (int i = 0; i < AddedCSharpObjects.Count && i < top; i++) sb.Append("  ").AppendLine(AddedCSharpObjects[i].ToString());
            }
            if (AddedRegistryReferences.Count > 0)
            {
                sb.AppendLine("Registry references added (Lua objects C# started holding):");
                for (int i = 0; i < AddedRegistryReferences.Count && i < top; i++)
                {
                    sb.Append("  ").AppendLine(AddedRegistryReferences[i].ToString());
                }
            }
            return sb.ToString();
        }
    }

#if !XLUA_GENERAL
    [UnityEngine.Scripting.Preserve]
#endif
    public static class LuaMemoryDiagnostics
    {
        // Takes a snapshot of everything reachable in the LuaEnv. Call it on the thread that runs
        // the LuaEnv. Safe from C# code that Lua called, from a finalizer and inside a coroutine:
        // the native walk stops the collector and the debug hook while it runs.
        public static LuaMemorySnapshot TakeMemorySnapshot(this LuaEnv env, LuaMemorySnapshotOptions options = null)
        {
            if (env == null) throw new ArgumentNullException("env");
            options = options ?? new LuaMemorySnapshotOptions();
            if (options.CollectCSharpGarbage)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                env.Tick();
            }
#if THREAD_SAFE || HOTFIX_ENABLE
            lock (env.luaEnvLock)
#endif
            {
                if (options.FullGc) env.FullGc();
                RealStatePtr L = env.L;
                IntPtr buffer;
                int size, status;
                DateTime time = DateTime.Now;
                try
                {
                    status = LuaAPI.xlua_memory_snapshot(L, Math.Max(0, options.MaxObjects), out buffer, out size);
                }
                catch (EntryPointNotFoundException)
                {
                    throw new NotSupportedException("the xlua native plugin was built without the memory snapshot "
                        + "(xlua_memory_snapshot). Rebuild it with XLUA_MEMORY_SNAPSHOT=ON, for example "
                        + "XLUA_MEMORY_SNAPSHOT=ON build/make_android_lua55.sh for a development Android build "
                        + "(the option is off by default for Android, OpenHarmony, UWP and Switch; see build/README.md)");
                }
                if (status != 0)
                {
                    throw new LuaException(status == 2 ? "memory snapshot: out of native memory"
                        : status == 3 ? "memory snapshot: out of Lua memory" : "memory snapshot failed: " + status);
                }
                byte[] data = new byte[size];
                try
                {
                    Marshal.Copy(buffer, data, 0, size);
                }
                finally
                {
                    LuaAPI.xlua_memory_snapshot_free(buffer);
                }
                var snapshot = new LuaMemorySnapshot(data, time);
                snapshot.Attach(env, options);
                return snapshot;
            }
        }

#if !XLUA_GENERAL
        [UnityEngine.Scripting.Preserve]
#endif
        public static LuaMemoryDiff Diff(LuaMemorySnapshot before, LuaMemorySnapshot after)
        {
            if (before == null) throw new ArgumentNullException("before");
            if (after == null) throw new ArgumentNullException("after");
            return new LuaMemoryDiff(before, after);
        }

        // The LuaEnvs that are alive and not disposed, for tools such as the Editor window.
        public static IList<LuaEnv> LiveEnvironments()
        {
            var result = new List<LuaEnv>();
            foreach (var translator in ObjectTranslatorPool.Instance.Translators())
            {
                LuaEnv env = translator.luaEnv;
                if (env != null && env.rawL != RealStatePtr.Zero && !result.Contains(env)) result.Add(env);
            }
            return result;
        }

        // For xlua.memory: any table of the LuaEnv identifies it, since Lua code has no LuaEnv.
#if !XLUA_GENERAL
        [UnityEngine.Scripting.Preserve]
#endif
        public static LuaMemorySnapshot TakeMemorySnapshotFromLua(LuaTable anyTable, bool fullGc)
        {
            if (anyTable == null) throw new ArgumentNullException("anyTable");
            using (anyTable)
            {
                var options = new LuaMemorySnapshotOptions();
                options.FullGc = fullGc;
                return anyTable.Env.TakeMemorySnapshot(options);
            }
        }

#if !XLUA_GENERAL
        [UnityEngine.Scripting.Preserve]
#endif
        public static string Report(object snapshotOrDiff, int top)
        {
            var snapshot = snapshotOrDiff as LuaMemorySnapshot;
            if (snapshot != null) return snapshot.Report(top);
            var diff = snapshotOrDiff as LuaMemoryDiff;
            if (diff != null) return diff.Report(top);
            throw new ArgumentException("expected a LuaMemorySnapshot or a LuaMemoryDiff");
        }
    }
}
#endif
