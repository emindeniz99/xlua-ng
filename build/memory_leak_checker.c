/*
 *Tencent is pleased to support the open source community by making xLua available.
 *Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 *Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 *http://opensource.org/licenses/MIT
 *Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

/*
 * This file has two parts:
 *
 *  - The leak checker (xlua_report_table_size, xlua_report_object_relationship,
 *    xlua_registry_pointer, xlua_global_pointer) walks Lua's internal object list
 *    through the internal headers. It exists on Lua 5.3 to 5.5 only and is used
 *    by General/LuaMemoryLeakChecker. CMake defines XLUA_NO_LEAK_CHECKER, which
 *    leaves it out, when the XLUA_LEAK_CHECKER option is off.
 *  - The memory snapshot (xlua_memory_snapshot, xlua_memory_snapshot_free) walks
 *    everything reachable from the roots through the public C API, so it also
 *    covers LuaJIT, userdata and objects with finalizers. LuaMemoryDiagnostics in
 *    Assets/XLua/Src/LuaMemorySnapshot.cs reads its result. CMake defines
 *    XLUA_NO_MEMORY_SNAPSHOT, which leaves it out, when the XLUA_MEMORY_SNAPSHOT
 *    option is off (the default for Android, OpenHarmony, UWP and Switch);
 *    builds without CMake (WebGL, the source plugin) keep it.
 */

#define LUA_LIB

/* XLUA_AMALGAMATION: the Unity WebGL build compiles all sources as one translation
   unit and has no include paths, so it includes the Lua headers before this file. */
#if !defined(XLUA_AMALGAMATION)
#include "lua.h"
#include "lauxlib.h"
#include "lualib.h"
#endif
#include <stdio.h>
#include <string.h>
#include <stdint.h>
#include <stdlib.h>
#if !defined(XLUA_AMALGAMATION) && LUA_VERSION_NUM >= 503
#include "ltable.h"
#include "lstate.h"
#include "lobject.h"
#include "lapi.h"
#include "lgc.h"
#include "lfunc.h"
#endif
#if LUA_VERSION_NUM >= 503
#include "xlua_core.h" /* the protected entry of a core state (LuaOptions.MemoryLimit) */
#endif

/* the internal-header leak checker: not on LuaJIT, and not with XLUA_NO_LEAK_CHECKER
 * (CMake XLUA_LEAK_CHECKER=OFF) */
#if LUA_VERSION_NUM >= 503 && !defined(XLUA_NO_LEAK_CHECKER)

#undef gnodelast  /* Lua 5.5 lgc.c defines it too, which matters when both are one translation unit */
#define gnodelast(h)	gnode(h, cast(size_t, sizenode(h)))

static int table_size (lua_State *L, Table *h, int fast)
{
	(void)L; /* only Lua 5.5's luaH_getn takes it */
	if (fast)
	{
#if LUA_VERSION_NUM >= 505
        return (int)sizenode(h) + (int)h->asize;
#elif LUA_VERSION_NUM >= 504
		return (int)sizenode(h) + (int)h->alimit;
#else
		return (int)sizenode(h) + (int)h->sizearray;
#endif
	}
	else
	{
		Node *n, *limit = gnodelast(h);
#if LUA_VERSION_NUM >= 505
        int i = (int)luaH_getn(L, h);
#else
		int i = (int)luaH_getn(h);
#endif
		for (n = gnode(h, 0); n < limit; n++)
		{ 
			if (!ttisnil(gval(n)))
			{
				i++;
			}
		}
		return i;
	}
}

typedef void (*TableSizeReport) (const void *p, int size);

// type: 1: value of table(key is string), 2: value of table(key is number), 3: key of table, 4: metatable of table, 5: upvalue of closure
typedef void (*ObjectRelationshipReport) (const void *parent, const void *child, int type, const char *key, double d, const char *key2);

LUA_API void xlua_report_table_size(lua_State *L, TableSizeReport cb, int fast)
{
	GCObject *p = G(L)->allgc;
	while (p != NULL)
	{
		if (p->tt == LUA_TTABLE)
		{
			Table *h = gco2t(p);
			cb(h, table_size(L, h, fast));
		}
		p = p->next;
	}
}


static void report_table(lua_State *L, Table *h, ObjectRelationshipReport cb)
{
	Node *n, *limit = gnodelast(h);
    unsigned int i;
	(void)L; /* unused on Lua 5.3 */

	if (h->metatable != NULL)
	{
		cb(h, h->metatable, 4, NULL, 0, NULL);
	}

#if LUA_VERSION_NUM >= 505
    for (i = 0; i < h->asize; i++)
#elif LUA_VERSION_NUM >= 504
    for (i = 0; i < h->alimit; i++)
#else
	for (i = 0; i < h->sizearray; i++)
#endif
	{
#if LUA_VERSION_NUM >= 505
        TValue arrayitem;
        arr2obj(h, i, &arrayitem);
        const TValue *item = &arrayitem;
#else
		const TValue *item = &h->array[i];
#endif
		if (ttistable(item))
		{
		    cb(h, gcvalue(item), 2, NULL, i + 1, NULL);
		}
	}

    for (n = gnode(h, 0); n < limit; n++)
	{
        if (!ttisnil(gval(n)))
        {
#if LUA_VERSION_NUM >= 504
            TValue keyvalue;
            getnodekey(L, &keyvalue, n);
            const TValue *key = &keyvalue;
#else
            const TValue *key = gkey(n);
#endif
			if (ttistable(key))
			{
				cb(h, gcvalue(key), 3, NULL, 0, NULL);
			}
            const TValue *value = gval(n);
			if (ttistable(value))
			{
				if (ttisstring(key))
				{
					cb(h, gcvalue(value), 1, getstr(tsvalue(key)), 0, NULL);
				}
				else if(ttisnumber(key))
				{
					cb(h, gcvalue(value), 2, NULL, nvalue(key), NULL);
				}
				else
				{
					// ???
#if LUA_VERSION_NUM >= 504
					cb(h, gcvalue(value), 1, NULL, novariant(key->tt_), NULL);
#else
					cb(h, gcvalue(value), 1, NULL, ttnov(key), NULL);
#endif
				}
			}
		}
    }
}


LUA_API void xlua_report_object_relationship(lua_State *L, ObjectRelationshipReport cb)
{
	GCObject *p = G(L)->allgc;
	lua_Debug ar;
	int i;
	const char *name;
	
	while (p != NULL)
	{
		if (p->tt == LUA_TTABLE)
		{
			Table *h = gco2t(p);
			report_table(L, h, cb);
		}
#if LUA_VERSION_NUM >= 504
		else if (p->tt == LUA_VLCL)
#else
		else if (p->tt == LUA_TLCL)
#endif
		{
			LClosure *cl = gco2lcl(p);
			lua_lock(L);
#if LUA_VERSION_NUM >= 504 && LUA_VERSION_RELEASE_NUM >= 50406
			setclLvalue2s(L, L->top.p, cl);
#else
			setclLvalue(L, L->top, cl);
#endif
			api_incr_top(L);
			lua_unlock(L);
			
			lua_pushvalue(L, -1);
			
			lua_getinfo(L, ">S", &ar);
			
			for (i=1;;i++)
			{
				name = lua_getupvalue(L,-1,i);
				if (name == NULL)
					break;
				const void *pv = lua_topointer(L, -1);
				
				/* A Lua closure's upvalue name is never empty: stripped
				 * bytecode reads "(no name)" ("(*no name)" in Lua 5.3). */
				if (LUA_TTABLE == lua_type(L, -1))
				{
					cb(cl, pv, 5, ar.short_src, ar.linedefined, name);
				}
				lua_pop(L, 1);
			}
			
			lua_pop(L, 1);
		}
		p = p->next;
	}
}

LUA_API void *xlua_registry_pointer(lua_State *L)
{
	return gcvalue(&G(L)->l_registry);
}

LUA_API void *xlua_global_pointer(lua_State *L)
{
	Table *reg = hvalue(&G(L)->l_registry);
	const TValue *global;
    lua_lock(L);
#if LUA_VERSION_NUM >= 505
    TValue globalvalue;
    luaH_getint(reg, LUA_RIDX_GLOBALS, &globalvalue);
    global = &globalvalue;
#else
	global = luaH_getint(reg, LUA_RIDX_GLOBALS);
#endif
	lua_unlock(L);
	return gcvalue(global);
}

#endif /* LUA_VERSION_NUM >= 503 && !XLUA_NO_LEAK_CHECKER */

/*
 * Memory snapshot: a breadth-first walk of everything reachable from the roots,
 * written into one buffer that the C# side copies and frees.
 *
 * The walk is a port of the marking algorithm of lua-snapshot
 * (https://github.com/cloudwu/lua-snapshot, snapshot.c at commit
 * f74f362e7b019cc0828ccfc48e9978afa8d700eb), under this notice:
 *
 *   Copyright (c) 2012 codingow.com
 *
 *   Permission is hereby granted, free of charge, to any person obtaining a copy
 *   of this software and associated documentation files (the "Software"), to deal
 *   in the Software without restriction, including without limitation the rights
 *   to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 *   copies of the Software, and to permit persons to whom the Software is
 *   furnished to do so, subject to the following conditions:
 *
 *   The above copyright notice and this permission notice shall be included in
 *   all copies or substantial portions of the Software.
 *
 *   THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 *   IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 *   FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 *   AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *   LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 *   OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 *   SOFTWARE.
 *
 * What it keeps from lua-snapshot: marking from the registry through the public
 * API only (lua_next, lua_getmetatable, lua_getupvalue, user values, thread
 * stacks and locals), skipping the weak side of weak tables and light C
 * functions. What differs:
 *
 *  - Breadth-first with a queue instead of recursion, so a long chain of tables
 *    cannot overflow the C stack, and the first reference to reach an object is
 *    on a shortest path from a root.
 *  - Nodes and edges go into native arrays and hash maps instead of a second Lua
 *    state; the only Lua allocations are the queue table and stack growth.
 *  - Reentrancy (https://github.com/Tencent/xLua/issues/1149): the collector is
 *    stopped and the debug hook removed for the duration of the walk, so no
 *    finalizer, hook or C# callback runs while it walks. It reads tables with
 *    raw access only, so no metamethod runs either. The walk runs in a protected
 *    call, so a Lua error (out of memory) becomes a status code instead of a
 *    longjmp through the C# caller. It works from inside a finalizer, a hook, a
 *    C function called from Lua and a coroutine.
 *  - It also reports userdata (https://github.com/Tencent/xLua/issues/1020),
 *    xLua's C# object references (the ObjectPool index in the userdata), objects
 *    with __gc (which Lua 5.4 and 5.5 keep on the finobj list that the leak
 *    checker above never visits), the roots _G and type metatables, every frame
 *    of every thread with its function, and table and closure sizes in bytes on
 *    Lua 5.3 to 5.5.
 *
 * Not covered: strings are counted (distinct reachable strings and their bytes)
 * but are not nodes; LuaJIT cdata and the values of ephemeron tables are treated
 * as unreachable and strong respectively; function prototypes and upvalue boxes
 * are not sized.
 */
#if !defined(XLUA_NO_MEMORY_SNAPSHOT)

#define LMS_MAGIC 0x534D4C58 /* "XLMS" in little-endian memory */
#define LMS_VERSION 1
#define LMS_KEY_MAX 200      /* longer strings are cut */

/* Result codes of xlua_memory_snapshot. */
#define LMS_OK 0
#define LMS_ERR_ARG 1
#define LMS_ERR_NOMEM 2      /* a native allocation failed, or the result would pass 2 GB */
#define LMS_ERR_LUA 3        /* a Lua error (out of Lua memory) stopped the walk */

/* Header flags. */
#define LMS_TRUNCATED 1          /* max_objects reached: objects beyond it are missing */
#define LMS_GC_WAS_RUNNING 2     /* the collector was running and was stopped for the walk */
#define LMS_THREAD_INCOMPLETE 4  /* a thread had no stack space left: some of its slots are missing */

/* Node flags. */
#define LMS_WEAK_KEYS 1
#define LMS_WEAK_VALUES 2
#define LMS_HAS_METATABLE 4
#define LMS_HAS_FINALIZER 8      /* the metatable has __gc */
#define LMS_C_FUNCTION 16
#define LMS_CSHARP_OBJECT 32     /* xLua userdata holding an ObjectPool index (in csobj) */
#define LMS_CSHARP_STRUCT 64     /* xLua userdata holding a copy of a C# struct */
#define LMS_CURRENT_THREAD 128   /* the thread the snapshot was taken on */

/* Edge kinds. */
#define LMS_EDGE_ROOT 1          /* from no node; the key names the root */
#define LMS_EDGE_VALUE 2         /* table value; the key is the table key */
#define LMS_EDGE_KEY 3           /* table key */
#define LMS_EDGE_METATABLE 4
#define LMS_EDGE_UPVALUE 5       /* key: upvalue name, number: index */
#define LMS_EDGE_USERVALUE 6     /* key: user value index */
#define LMS_EDGE_ENVIRONMENT 7   /* Lua 5.1 and LuaJIT function or userdata environment */
#define LMS_EDGE_STACK 8         /* key: slot index of a thread that is not running */
#define LMS_EDGE_LOCAL 9         /* key: "name @ source:line", number: frame level */
#define LMS_EDGE_FRAME 10        /* function of a call frame; key: "source:line", number: level */

/* Key types of an edge. */
#define LMS_KEY_NONE 0
#define LMS_KEY_STRING 1         /* key: string pool offset */
#define LMS_KEY_INTEGER 2        /* key: the integer */
#define LMS_KEY_NUMBER 3         /* number: the float */
#define LMS_KEY_BOOLEAN 4        /* key: 0 or 1 */
#define LMS_KEY_LIGHTUSERDATA 5  /* key: the pointer */
#define LMS_KEY_OBJECT 16        /* plus the Lua type; key: the object's address */

/* The buffer is a header, the nodes, the edges and the string pool, in this
 * order, in native byte order. A string is an int32 length and its bytes. */
typedef struct lms_header {
	int32_t magic, version, header_bytes, node_bytes, edge_bytes;
	int32_t node_count, edge_count, string_bytes, flags, lua_version;
	int64_t heap_bytes;       /* lua_gc count before the walk */
	int64_t string_total;     /* bytes of the distinct reachable strings */
	int32_t string_count;     /* distinct reachable strings */
	int32_t reserved;
} lms_header;

typedef struct lms_node {
	uint64_t address;         /* lua_topointer */
	int64_t size;             /* bytes (tables and closures on Lua 5.3 to 5.5, userdata), or -1 */
	int32_t type;             /* LUA_TTABLE, LUA_TFUNCTION, LUA_TUSERDATA or LUA_TTHREAD */
	int32_t flags;
	int32_t count;            /* table entries, upvalues, user values or stack slots */
	int32_t csobj;            /* ObjectPool index, or -1 */
	int32_t info;             /* string offset (function source, thread status), or -1 */
	int32_t parent_edge;      /* the edge the walk reached it through first */
} lms_node;

typedef struct lms_edge {
	int32_t from;             /* node index, or -1 for a root */
	int32_t to;
	int32_t kind;
	int32_t key_type;
	int64_t key;
	double number;
} lms_edge;

/* Compile-time size checks that also work when WebGL compiles this as C++. */
typedef char lms_header_size_check[sizeof(lms_header) == 64 ? 1 : -1];
typedef char lms_node_size_check[sizeof(lms_node) == 40 ? 1 : -1];
typedef char lms_edge_size_check[sizeof(lms_edge) == 32 ? 1 : -1];

/* Open addressing map from a pointer to an int, for object identity and for
 * strings. Keys are never NULL. */
typedef struct lms_slot {
	const void *key;
	int32_t value;
} lms_slot;

typedef struct lms_map {
	lms_slot *slots;
	size_t mask;              /* capacity - 1, or 0 while empty */
	size_t count;
} lms_map;

typedef struct lms_ctx {
	lms_node *nodes;
	lms_edge *edges;
	char *strings;
	int32_t node_count, node_cap, edge_count, edge_cap, string_bytes, string_cap;
	lms_map objects;          /* object address -> node index */
	lms_map keys;             /* string data -> pool offset */
	lms_map seen_strings;     /* string data of the reachable strings */
	int64_t string_total;
	int32_t max_objects;
	int32_t flags;
	int oom;
	int queue, mode_key, gc_key, tag_key, globals;  /* stack slots inside the walk */
} lms_ctx;

/* The whole buffer stays under 1 GB, so every count and offset fits an int32. */
#define LMS_MAX_BYTES ((int64_t)1 << 30)

static size_t lms_hash(const void *p)
{
	uint64_t x = (uint64_t)(uintptr_t)p;
	x ^= x >> 33;
	x *= 0xff51afd7ed558ccdULL;
	x ^= x >> 33;
	return (size_t)x;
}

static int32_t lms_map_get(const lms_map *m, const void *key)
{
	size_t i;
	if (m->mask == 0) return -1;
	for (i = lms_hash(key) & m->mask; m->slots[i].key != NULL; i = (i + 1) & m->mask)
	{
		if (m->slots[i].key == key) return m->slots[i].value;
	}
	return -1;
}

static int lms_map_put(lms_map *m, const void *key, int32_t value)
{
	size_t i;
	if ((m->count + 1) * 2 > m->mask + 1)
	{
		size_t cap = m->mask ? (m->mask + 1) * 2 : 1024, j;
		lms_slot *slots;
		if (cap > SIZE_MAX / sizeof(lms_slot)) return -1;
		slots = (lms_slot *)calloc(cap, sizeof(lms_slot));
		if (slots == NULL) return -1;
		for (j = 0; m->mask != 0 && j <= m->mask; j++)
		{
			if (m->slots[j].key != NULL)
			{
				for (i = lms_hash(m->slots[j].key) & (cap - 1); slots[i].key != NULL; i = (i + 1) & (cap - 1)) {}
				slots[i] = m->slots[j];
			}
		}
		free(m->slots);
		m->slots = slots;
		m->mask = cap - 1;
	}
	for (i = lms_hash(key) & m->mask; m->slots[i].key != NULL; i = (i + 1) & m->mask) {}
	m->slots[i].key = key;
	m->slots[i].value = value;
	m->count++;
	return 0;
}

/* The capacity for count + more items of elem bytes: cap itself when it is
 * enough, -1 when the array would pass LMS_MAX_BYTES. */
static int64_t lms_capacity(int32_t cap, int32_t count, size_t elem, int32_t more)
{
	int64_t want = (int64_t)count + more, grown = cap ? cap : 256;
	if (want <= cap) return cap;
	while (grown < want) grown *= 2;
	return grown * (int64_t)elem > LMS_MAX_BYTES ? -1 : grown;
}

static int lms_grow_nodes(lms_ctx *c)
{
	int64_t cap = lms_capacity(c->node_cap, c->node_count, sizeof(lms_node), 1);
	lms_node *grown;
	if (cap == c->node_cap) return 0;
	grown = cap < 0 ? NULL : (lms_node *)realloc(c->nodes, (size_t)cap * sizeof(lms_node));
	if (grown == NULL) return -1;
	c->nodes = grown;
	c->node_cap = (int32_t)cap;
	return 0;
}

static int lms_grow_edges(lms_ctx *c)
{
	int64_t cap = lms_capacity(c->edge_cap, c->edge_count, sizeof(lms_edge), 1);
	lms_edge *grown;
	if (cap == c->edge_cap) return 0;
	grown = cap < 0 ? NULL : (lms_edge *)realloc(c->edges, (size_t)cap * sizeof(lms_edge));
	if (grown == NULL) return -1;
	c->edges = grown;
	c->edge_cap = (int32_t)cap;
	return 0;
}

static int lms_grow_strings(lms_ctx *c, int32_t more)
{
	int64_t cap = lms_capacity(c->string_cap, c->string_bytes, 1, more);
	char *grown;
	if (cap == c->string_cap) return 0;
	grown = cap < 0 ? NULL : (char *)realloc(c->strings, (size_t)cap);
	if (grown == NULL) return -1;
	c->strings = grown;
	c->string_cap = (int32_t)cap;
	return 0;
}

/* Appends a string to the pool and returns its offset, or -1. */
static int32_t lms_add_string(lms_ctx *c, const char *s, size_t len)
{
	int32_t offset = c->string_bytes, n;
	if (len > LMS_KEY_MAX) len = LMS_KEY_MAX;
	n = (int32_t)len;
	if (lms_grow_strings(c, n + 4) != 0)
	{
		c->oom = 1;
		return -1;
	}
	memcpy(c->strings + offset, &n, 4);
	if (n > 0) memcpy(c->strings + offset + 4, s, len);
	c->string_bytes += n + 4;
	return offset;
}

/* A Lua string or a constant C string, stored once per address. */
static int32_t lms_intern(lms_ctx *c, const char *s, size_t len)
{
	int32_t offset = lms_map_get(&c->keys, s);
	if (offset >= 0) return offset;
	offset = lms_add_string(c, s, len);
	if (offset >= 0 && lms_map_put(&c->keys, s, offset) != 0) c->oom = 1;
	return offset;
}

/* The key of an edge. */
typedef struct lms_key {
	int type;
	int64_t key;
	double number;
} lms_key;

static int32_t lms_add_edge(lms_ctx *c, int32_t from, int32_t to, int kind, lms_key k)
{
	lms_edge *e;
	if (lms_grow_edges(c) != 0)
	{
		c->oom = 1;
		return -1;
	}
	e = &c->edges[c->edge_count];
	e->from = from;
	e->to = to;
	e->kind = kind;
	e->key_type = k.type;
	e->key = k.key;
	e->number = k.number;
	return c->edge_count++;
}

/* The value at idx as a key: what lua_next left below the value. */
static lms_key lms_describe(lms_ctx *c, lua_State *L, int idx)
{
	lms_key k;
	size_t len;
	const char *s;
	k.type = LMS_KEY_NONE;
	k.key = 0;
	k.number = 0;
	switch (lua_type(L, idx))
	{
	case LUA_TSTRING:
		s = lua_tolstring(L, idx, &len); /* already a string: no conversion, no allocation */
		k.key = lms_intern(c, s, len);
		k.type = k.key >= 0 ? LMS_KEY_STRING : LMS_KEY_NONE;
		break;
	case LUA_TNUMBER:
#if LUA_VERSION_NUM >= 503
		if (lua_isinteger(L, idx))
		{
			k.type = LMS_KEY_INTEGER;
			k.key = (int64_t)lua_tointeger(L, idx);
			break;
		}
#endif
		k.type = LMS_KEY_NUMBER;
		k.number = (double)lua_tonumber(L, idx);
#if LUA_VERSION_NUM < 503
		/* Lua 5.1 and LuaJIT have only floats: report integral ones as integers, as
		 * the registry references of luaL_ref are. */
		if (k.number >= -9007199254740992.0 && k.number <= 9007199254740992.0
			&& (double)(int64_t)k.number == k.number)
		{
			k.type = LMS_KEY_INTEGER;
			k.key = (int64_t)k.number;
		}
#endif
		break;
	case LUA_TBOOLEAN:
		k.type = LMS_KEY_BOOLEAN;
		k.key = lua_toboolean(L, idx);
		break;
	case LUA_TLIGHTUSERDATA:
		k.type = LMS_KEY_LIGHTUSERDATA;
		k.key = (int64_t)(intptr_t)lua_touserdata(L, idx);
		break;
	default:
		k.type = LMS_KEY_OBJECT + lua_type(L, idx);
		k.key = (int64_t)(intptr_t)lua_topointer(L, idx);
		break;
	}
	return k;
}

static lms_key lms_name_key(lms_ctx *c, const char *name, double number)
{
	lms_key k;
	k.key = lms_intern(c, name, strlen(name));
	k.type = k.key >= 0 ? LMS_KEY_STRING : LMS_KEY_NONE;
	k.number = number;
	return k;
}

/* A formatted key (frame and local descriptions), not shared. */
static lms_key lms_text_key(lms_ctx *c, const char *text, double number)
{
	lms_key k;
	k.key = lms_add_string(c, text, strlen(text));
	k.type = k.key >= 0 ? LMS_KEY_STRING : LMS_KEY_NONE;
	k.number = number;
	return k;
}

static lms_key lms_index_key(int64_t index)
{
	lms_key k;
	k.type = index > 0 ? LMS_KEY_INTEGER : LMS_KEY_NONE;
	k.key = index;
	k.number = 0;
	return k;
}

static int lms_is_light_cfunction(lua_State *L)
{
#if LUA_VERSION_NUM >= 502
	if (lua_iscfunction(L, -1))
	{
		if (lua_getupvalue(L, -1, 1) == NULL) return 1;
		lua_pop(L, 1);
	}
#else
	(void)L; /* every Lua 5.1 and LuaJIT C function is an object */
#endif
	return 0;
}

/* Records a reference from node `from` to the value on top of the stack and
 * pops it. A table, function, userdata or thread seen for the first time
 * becomes a node and goes to the back of the queue. */
static void lms_add(lms_ctx *c, lua_State *L, int32_t from, int kind, lms_key k)
{
	int type = lua_type(L, -1);
	const void *p;
	int32_t node;
	lms_node *n;
	switch (type)
	{
	case LUA_TTABLE:
	case LUA_TUSERDATA:
	case LUA_TTHREAD:
		break;
	case LUA_TFUNCTION:
		if (lms_is_light_cfunction(L))
		{
			lua_pop(L, 1);
			return;
		}
		break;
	case LUA_TSTRING:
	{
		size_t len;
		const char *s = lua_tolstring(L, -1, &len);
		if (!c->oom && lms_map_get(&c->seen_strings, s) < 0)
		{
			if (lms_map_put(&c->seen_strings, s, 1) != 0) c->oom = 1;
			c->string_total += (int64_t)len;
		}
		lua_pop(L, 1);
		return;
	}
	default:
		lua_pop(L, 1);
		return;
	}
	p = lua_topointer(L, -1);
	node = lms_map_get(&c->objects, p);
	if (node >= 0 || c->oom)
	{
		if (node >= 0) lms_add_edge(c, from, node, kind, k);
		lua_pop(L, 1);
		return;
	}
	if (c->max_objects > 0 && c->node_count >= c->max_objects)
	{
		c->flags |= LMS_TRUNCATED;
		lua_pop(L, 1);
		return;
	}
	if (lms_grow_nodes(c) != 0 || lms_map_put(&c->objects, p, c->node_count) != 0)
	{
		c->oom = 1;
		lua_pop(L, 1);
		return;
	}
	node = c->node_count++;
	n = &c->nodes[node];
	n->address = (uint64_t)(uintptr_t)p;
	n->size = -1;
	n->type = type;
	n->flags = 0;
	n->count = 0;
	n->csobj = -1;
	n->info = -1;
	n->parent_edge = lms_add_edge(c, from, node, kind, k);
	lua_rawseti(L, c->queue, node + 1); /* node i waits at queue[i + 1]; pops the value */
}

/* Reads __gc, and __mode when weak, of the metatable on top of the stack. */
static void lms_metatable_flags(lms_ctx *c, lua_State *L, int32_t node, int weak)
{
	lua_pushvalue(L, c->gc_key);
	lua_rawget(L, -2);
	if (!lua_isnil(L, -1)) c->nodes[node].flags |= LMS_HAS_FINALIZER;
	lua_pop(L, 1);
	if (weak)
	{
		lua_pushvalue(L, c->mode_key);
		lua_rawget(L, -2);
		if (lua_type(L, -1) == LUA_TSTRING)
		{
			const char *mode = lua_tostring(L, -1);
			if (strchr(mode, 'k') != NULL) c->nodes[node].flags |= LMS_WEAK_KEYS;
			if (strchr(mode, 'v') != NULL) c->nodes[node].flags |= LMS_WEAK_VALUES;
		}
		lua_pop(L, 1);
	}
	c->nodes[node].flags |= LMS_HAS_METATABLE;
}

static void lms_visit_table(lms_ctx *c, lua_State *L, int32_t node)
{
	int32_t count = 0, flags;
	if (lua_getmetatable(L, -1))
	{
		lms_metatable_flags(c, L, node, 1);
		lms_add(c, L, node, LMS_EDGE_METATABLE, lms_index_key(0));
	}
	flags = c->nodes[node].flags;
	lua_pushnil(L);
	while (lua_next(L, -2) != 0)
	{
		count++;
		if (c->oom)
		{
			lua_pop(L, 2);
			break;
		}
		if (flags & LMS_WEAK_VALUES)
		{
			lua_pop(L, 1);
		}
		else
		{
			lms_add(c, L, node, LMS_EDGE_VALUE, lms_describe(c, L, -2));
		}
		if (!(flags & LMS_WEAK_KEYS))
		{
			lua_pushvalue(L, -1);
			lms_add(c, L, node, LMS_EDGE_KEY, lms_index_key(0));
		}
	}
	c->nodes[node].count = count;
#if LUA_VERSION_NUM >= 505
	/* luaH_size takes a non-const Table; it only reads it. */
	c->nodes[node].size = (int64_t)luaH_size((Table *)(uintptr_t)lua_topointer(L, -1)); // NOLINT(performance-no-int-to-ptr): drops the const of lua_topointer's result
#elif LUA_VERSION_NUM >= 503
	{
		const Table *h = (const Table *)lua_topointer(L, -1);
#if LUA_VERSION_NUM >= 504
		size_t array = luaH_realasize(h);
#else
		size_t array = h->sizearray;
#endif
		c->nodes[node].size = (int64_t)(sizeof(Table) + array * sizeof(TValue)
			+ (isdummy(h) ? 0 : (size_t)sizenode(h) * sizeof(Node)));
	}
#endif
}

static void lms_visit_userdata(lms_ctx *c, lua_State *L, int32_t node)
{
	int32_t count = 0;
#if LUA_VERSION_NUM >= 502
	size_t size = lua_rawlen(L, -1);
#else
	size_t size = lua_objlen(L, -1);
#endif
	c->nodes[node].size = (int64_t)size;
	if (lua_getmetatable(L, -1))
	{
		lms_metatable_flags(c, L, node, 0);
		/* xlua_pushcsobj: an int ObjectPool index, under a metatable with the xLua tag;
		 * struct copies (xlua_pushstruct) have the tag and a larger block. */
		lua_pushvalue(L, c->tag_key);
		lua_rawget(L, -2);
		if (!lua_isnil(L, -1))
		{
			if (size == sizeof(int))
			{
				const int *index = (const int *)lua_touserdata(L, -3);
				c->nodes[node].csobj = *index;
				c->nodes[node].flags |= LMS_CSHARP_OBJECT;
			}
			else
			{
				c->nodes[node].flags |= LMS_CSHARP_STRUCT;
			}
		}
		lua_pop(L, 1);
		lms_add(c, L, node, LMS_EDGE_METATABLE, lms_index_key(0));
	}
#if LUA_VERSION_NUM >= 504
	for (;;)
	{
		if (lua_getiuservalue(L, -1, count + 1) == LUA_TNONE)
		{
			lua_pop(L, 1);
			break;
		}
		count++;
		lms_add(c, L, node, LMS_EDGE_USERVALUE, lms_index_key(count));
	}
#elif LUA_VERSION_NUM >= 502
	lua_getuservalue(L, -1);
	if (lua_isnil(L, -1))
	{
		lua_pop(L, 1);
	}
	else
	{
		count = 1;
		lms_add(c, L, node, LMS_EDGE_USERVALUE, lms_index_key(1));
	}
#else
	lua_getfenv(L, -1);
	if (lua_isnil(L, -1) || lua_rawequal(L, -1, c->globals))
	{
		lua_pop(L, 1); /* the default environment is _G, which is a root anyway */
	}
	else
	{
		count = 1;
		lms_add(c, L, node, LMS_EDGE_ENVIRONMENT, lms_index_key(0));
	}
#endif
	c->nodes[node].count = count;
}

static void lms_visit_function(lms_ctx *c, lua_State *L, int32_t node)
{
	int32_t i;
	char text[LUA_IDSIZE + 24];
	if (lua_iscfunction(L, -1))
	{
		c->nodes[node].flags |= LMS_C_FUNCTION;
	}
	else
	{
		lua_Debug ar;
		lua_pushvalue(L, -1);
		if (lua_getinfo(L, ">S", &ar))
		{
			(void)snprintf(text, sizeof(text), "%s:%d", ar.short_src, ar.linedefined);
			c->nodes[node].info = lms_add_string(c, text, strlen(text));
		}
	}
	for (i = 1;; i++)
	{
		const char *name = lua_getupvalue(L, -1, i);
		if (name == NULL) break;
		lms_add(c, L, node, LMS_EDGE_UPVALUE, lms_name_key(c, name, i));
	}
	c->nodes[node].count = i - 1;
#if LUA_VERSION_NUM == 501
	lua_getfenv(L, -1);
	if (lua_isnil(L, -1) || lua_rawequal(L, -1, c->globals))
	{
		lua_pop(L, 1);
	}
	else
	{
		lms_add(c, L, node, LMS_EDGE_ENVIRONMENT, lms_index_key(0));
	}
#elif LUA_VERSION_NUM >= 503
	{
		int up = c->nodes[node].count;
		c->nodes[node].size = (c->nodes[node].flags & LMS_C_FUNCTION) ? sizeCclosure(up) : sizeLclosure(up);
	}
#endif
}

/* Records the value on top of co's stack: it is moved to L first. */
static void lms_move(lms_ctx *c, lua_State *co, lua_State *L, int32_t node, int kind, lms_key k)
{
	lua_xmove(co, L, 1); /* does nothing when co is L */
	lms_add(c, L, node, kind, k);
}

static void lms_visit_thread(lms_ctx *c, lua_State *L, int32_t node)
{
	lua_State *co = lua_tothread(L, -1);
	lua_Debug ar;
	int level = 0, i;
	const char *status;
	char text[LUA_IDSIZE + 64];
	if (co == L)
	{
		/* Level 0 is the walk itself. Values that the C caller left on the stack
		 * outside any call frame are not visible from here. */
		c->nodes[node].flags |= LMS_CURRENT_THREAD;
		status = "running";
		level = 1;
	}
	else
	{
		int top;
		switch (lua_status(co))
		{
		case LUA_YIELD: status = "suspended"; break;
		case 0: status = lua_getstack(co, 0, &ar) ? "normal" : (lua_gettop(co) == 0 ? "dead" : "suspended"); break;
		default: status = "dead"; break;
		}
		if (!lua_checkstack(co, 2))
		{
			c->flags |= LMS_THREAD_INCOMPLETE;
			c->nodes[node].info = lms_add_string(c, status, strlen(status));
			return;
		}
		top = lua_gettop(co);
		c->nodes[node].count = top;
		for (i = 1; i <= top && !c->oom; i++)
		{
			lua_pushvalue(co, i);
			lms_move(c, co, L, node, LMS_EDGE_STACK, lms_index_key(i));
		}
	}
	c->nodes[node].info = lms_add_string(c, status, strlen(status));
	for (; !c->oom && lua_getstack(co, level, &ar); level++)
	{
		if (!lua_checkstack(co, 2))
		{
			c->flags |= LMS_THREAD_INCOMPLETE;
			break;
		}
		if (!lua_getinfo(co, "Slf", &ar)) continue;
		(void)snprintf(text, sizeof(text), "%s:%d", ar.short_src, ar.currentline);
		lms_move(c, co, L, node, LMS_EDGE_FRAME, lms_text_key(c, text, level));
		/* Locals and temporaries (1, 2, ...), then varargs (-1, -2, ...). */
		for (i = 1; !c->oom; i++)
		{
			const char *name = lua_getlocal(co, &ar, i);
			if (name == NULL) break;
			(void)snprintf(text, sizeof(text), "%s @ %s:%d", name, ar.short_src, ar.currentline);
			lms_move(c, co, L, node, LMS_EDGE_LOCAL, lms_text_key(c, text, level));
		}
		for (i = -1; !c->oom; i--)
		{
			const char *name = lua_getlocal(co, &ar, i);
			if (name == NULL) break;
			(void)snprintf(text, sizeof(text), "%s @ %s:%d", name, ar.short_src, ar.currentline);
			lms_move(c, co, L, node, LMS_EDGE_LOCAL, lms_text_key(c, text, level));
		}
	}
}

LUA_API void *xlua_tag(void);

/* Visits the queued nodes from index next on, which queues the nodes they
 * reach; returns the node count when the queue is empty. */
static int32_t lms_drain(lms_ctx *c, lua_State *L, int32_t next)
{
	for (; next < c->node_count && !c->oom; next++)
	{
		lua_rawgeti(L, c->queue, next + 1);
		switch (c->nodes[next].type)
		{
		case LUA_TTABLE: lms_visit_table(c, L, next); break;
		case LUA_TUSERDATA: lms_visit_userdata(c, L, next); break;
		case LUA_TFUNCTION: lms_visit_function(c, L, next); break;
		default: lms_visit_thread(c, L, next); break;
		}
		lua_pop(L, 1);
	}
	return next;
}

/* The protected part; argument 1 is the context. */
static int lms_walk(lua_State *L)
{
	static const char *const metatable_roots[] = {"metatable(nil)", "metatable(boolean)",
		"metatable(lightuserdata)", "metatable(number)", "metatable(string)", "metatable(function)",
		"metatable(thread)"};
	lms_ctx *c = (lms_ctx *)lua_touserdata(L, 1);
	int32_t next;
	int k;
	lua_settop(L, 1);
	luaL_checkstack(L, LUA_MINSTACK + 8, "memory snapshot");
	lua_createtable(L, 1024, 0);
	c->queue = lua_gettop(L);
	lua_pushliteral(L, "__mode");
	c->mode_key = lua_gettop(L);
	lua_pushliteral(L, "__gc");
	c->gc_key = lua_gettop(L);
	lua_pushlightuserdata(L, xlua_tag());
	c->tag_key = lua_gettop(L);
#if LUA_VERSION_NUM == 501
	lua_pushvalue(L, LUA_GLOBALSINDEX);
#else
	lua_rawgeti(L, LUA_REGISTRYINDEX, LUA_RIDX_GLOBALS);
#endif
	c->globals = lua_gettop(L);

	/* Roots, _G first so that the paths reported go through it. The calling
	 * thread comes last, after everything the others reach: otherwise a stale
	 * temporary in one of its frames would give an object a path through the
	 * stack although _G also reaches it. */
	lua_pushvalue(L, c->globals);
	lms_add(c, L, -1, LMS_EDGE_ROOT, lms_name_key(c, "_G", 0));
	lua_pushvalue(L, LUA_REGISTRYINDEX);
	lms_add(c, L, -1, LMS_EDGE_ROOT, lms_name_key(c, "registry", 0));
	for (k = 0; k < 7; k++)
	{
		switch (k)
		{
		case 0: lua_pushnil(L); break;
		case 1: lua_pushboolean(L, 0); break;
		case 2: lua_pushlightuserdata(L, NULL); break;
		case 3: lua_pushnumber(L, 0); break;
		case 4: lua_pushvalue(L, c->mode_key); break;
		case 5: lua_pushcfunction(L, lms_walk); break;
		default: lua_pushthread(L); break;
		}
		if (lua_getmetatable(L, -1))
		{
			lms_add(c, L, -1, LMS_EDGE_ROOT, lms_name_key(c, metatable_roots[k], 0));
		}
		lua_pop(L, 1);
	}

	next = lms_drain(c, L, 0);
	lua_pushthread(L);
	lms_add(c, L, -1, LMS_EDGE_ROOT, lms_name_key(c, "thread", 0));
	lms_drain(c, L, next);
	return 0;
}

static void lms_free_ctx(lms_ctx *c)
{
	free(c->nodes);
	free(c->edges);
	free(c->strings);
	free(c->objects.slots);
	free(c->keys.slots);
	free(c->seen_strings.slots);
}

/*
 * Walks everything reachable from _G, the registry, the type metatables and the
 * calling thread. max_objects > 0 stops recording new objects after that many
 * (header flag LMS_TRUNCATED). On LMS_OK, *out holds the buffer, to be freed
 * with xlua_memory_snapshot_free, and *out_size its size. It never raises a Lua
 * error and may be called from a finalizer, a hook or a C function Lua called.
 */
LUA_API int xlua_memory_snapshot(lua_State *L, int max_objects, void **out, int *out_size)
{
	lms_ctx c;
	lms_header h;
	lua_Hook hook;
	int hook_mask, hook_count, gc_running, top, status;
#if LUA_VERSION_NUM >= 503
	xlua_core_entry entry;
#endif
	size_t nodes, edges, total;
	char *buffer;

	if (L == NULL || out == NULL || out_size == NULL) return LMS_ERR_ARG;
	*out = NULL;
	*out_size = 0;
	if (!lua_checkstack(L, 4)) return LMS_ERR_LUA;
	memset(&c, 0, sizeof(c));
	memset(&h, 0, sizeof(h));
	c.max_objects = max_objects;

	/* No hook, finalizer or collector step may run during the walk. Inside a
	 * finalizer Lua 5.4 and 5.5 answer -1 and do not collect anyway; Lua 5.3 and
	 * LuaJIT answer 0 there. */
	hook = lua_gethook(L);
	hook_mask = lua_gethookmask(L);
	hook_count = lua_gethookcount(L);
	if (hook != NULL) lua_sethook(L, NULL, 0, 0);
	gc_running = lua_gc(L, LUA_GCISRUNNING, 0);
	if (gc_running > 0)
	{
		lua_gc(L, LUA_GCSTOP, 0);
		c.flags |= LMS_GC_WAS_RUNNING;
	}
	h.heap_bytes = (int64_t)lua_gc(L, LUA_GCCOUNT, 0) * 1024 + lua_gc(L, LUA_GCCOUNTB, 0);

	top = lua_gettop(L);
#if LUA_VERSION_NUM == 501
	status = lua_cpcall(L, lms_walk, &c);
#else
	/* A protected entry for a core state with a memory limit: called from C#
	 * code that Lua called, the allocator would otherwise grant the walk's
	 * allocations over the limit and the trampoline would raise the memory
	 * error afterwards. Under the entry they are refused, and the walk reports
	 * LMS_ERR_LUA instead. */
	xlua_core_enter(L, &entry);
	lua_pushcfunction(L, lms_walk);
	lua_pushlightuserdata(L, &c);
	status = lua_pcall(L, 1, 0, 0);
	xlua_core_leave(&entry);
#endif
	lua_settop(L, top); /* drops the error object, if any */

	if (gc_running > 0) lua_gc(L, LUA_GCRESTART, 0);
	if (hook != NULL) lua_sethook(L, hook, hook_mask, hook_count);

	if (status != 0 || c.oom)
	{
		lms_free_ctx(&c);
		return status != 0 ? LMS_ERR_LUA : LMS_ERR_NOMEM;
	}
	nodes = (size_t)c.node_count * sizeof(lms_node);
	edges = (size_t)c.edge_count * sizeof(lms_edge);
	total = sizeof(lms_header) + nodes + edges + (size_t)c.string_bytes;
	buffer = total <= (size_t)INT32_MAX ? (char *)malloc(total) : NULL;
	if (buffer == NULL)
	{
		lms_free_ctx(&c);
		return LMS_ERR_NOMEM;
	}
	h.magic = LMS_MAGIC;
	h.version = LMS_VERSION;
	h.header_bytes = (int32_t)sizeof(lms_header);
	h.node_bytes = (int32_t)sizeof(lms_node);
	h.edge_bytes = (int32_t)sizeof(lms_edge);
	h.node_count = c.node_count;
	h.edge_count = c.edge_count;
	h.string_bytes = c.string_bytes;
	h.flags = c.flags;
	h.lua_version = LUA_VERSION_NUM;
	h.string_total = c.string_total;
	h.string_count = (int32_t)c.seen_strings.count;
	memcpy(buffer, &h, sizeof(h));
	if (nodes) memcpy(buffer + sizeof(h), c.nodes, nodes);
	if (edges) memcpy(buffer + sizeof(h) + nodes, c.edges, edges);
	if (c.string_bytes) memcpy(buffer + sizeof(h) + nodes + edges, c.strings, (size_t)c.string_bytes);
	lms_free_ctx(&c);
	*out = buffer;
	*out_size = (int)total;
	return LMS_OK;
}

LUA_API void xlua_memory_snapshot_free(void *buffer)
{
	free(buffer);
}

#endif /* !XLUA_NO_MEMORY_SNAPSHOT */

#if (LUA_VERSION_NUM < 503 || defined(XLUA_NO_LEAK_CHECKER)) && defined(XLUA_NO_MEMORY_SNAPSHOT)
/* Without the leak checker (always so on LuaJIT) and the snapshot nothing else is
 * left here; ISO C wants a declaration. */
typedef int xlua_memory_leak_checker_empty;
#endif
