/*
 *Tencent is pleased to support the open source community by making xLua available.
 *Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 *Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 *http://opensource.org/licenses/MIT
 *Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

/* xlua_chunkc: the chunk compiler of bytecode-only builds (CMake XLUA_BYTECODE_ONLY,
 * build/README.md, "Bytecode-only builds"). A host program built from the vendored
 * Lua of the library, as luac is. It reads the built-in chunks of the C# runtime
 * from the C# sources, compiles each one and writes a C header with the bytecode,
 * which build/xlua.c includes; LuaEnv.LoadBuiltinChunk loads them from there.
 *
 *   xlua_chunkc [-s] [-D OPTION]... -o OUT.h FILE.cs...
 *
 * A built-in chunk is one C# verbatim string between two marker comments:
 *
 *   // <xlua-chunk name="NAME" chunkname="CHUNKNAME" option="OPTION">
 *   const string Field = @"...Lua source, "" for each double quote...";
 *   // </xlua-chunk>
 *
 * NAME is what LuaEnv asks the library for, CHUNKNAME the chunk name C# loads the
 * text with (also the source name in the bytecode's debug information). A chunk
 * with option= is compiled only when -D names that option; the others always.
 * -s strips the debug information. The header also records the hash C# compares
 * (FNV-1a of the text without carriage returns, LuaEnv.BuiltinChunkHash) and the
 * sizes the bytecode format depends on, which xlua.c checks against the target.
 * The C# text must be ASCII, so that its bytes are the same whatever encoding the
 * C# compiler reads the file with. */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "lua.h"
#include "lauxlib.h"

typedef struct buffer {
	char *data;
	size_t size;
	size_t capacity;
} buffer;

typedef struct chunk {
	char *name;
	char *chunkname;
	char *file;
	buffer text;
	buffer code;
	unsigned long hash;
} chunk;

static void fail(const char *what, const char *detail, const char *where) {
	fprintf(stderr, "xlua_chunkc: %s%s%s%s\n", what, detail ? detail : "", where ? " in " : "", where ? where : "");
	exit(1);
}

static void add(buffer *b, const void *p, size_t n) {
	if (b->size + n + 1 > b->capacity) {
		size_t capacity = b->capacity ? b->capacity : 4096;
		char *data;
		while (b->size + n + 1 > capacity) {
			capacity *= 2;
		}
		data = (char *)realloc(b->data, capacity);
		if (data == NULL) {
			fail("out of memory", NULL, NULL);
		}
		b->data = data;
		b->capacity = capacity;
	}
	memcpy(b->data + b->size, p, n);
	b->size += n;
	b->data[b->size] = '\0';
}

static buffer read_file(const char *path) {
	buffer b = {NULL, 0, 0};
	char block[65536];
	size_t n;
	FILE *f = fopen(path, "rb");
	if (f == NULL) {
		fail("cannot open ", path, NULL);
	}
	while ((n = fread(block, 1, sizeof(block), f)) > 0) {
		add(&b, block, n);
	}
	if (ferror(f)) {
		fail("cannot read ", path, NULL);
	}
	fclose(f);
	if (b.data == NULL) {
		add(&b, "", 0);
	}
	return b;
}

static char *copy(const char *p, size_t n) {
	char *s = (char *)malloc(n + 1);
	if (s == NULL) {
		fail("out of memory", NULL, NULL);
	}
	memcpy(s, p, n);
	s[n] = '\0';
	return s;
}

/* The value of key="..." in the marker between begin and end, or NULL. */
static char *attribute(const char *begin, const char *end, const char *key, const char *file) {
	size_t key_size = strlen(key);
	const char *p;
	for (p = begin; p + key_size + 2 <= end; p++) {
		if ((p == begin || p[-1] == ' ') && strncmp(p, key, key_size) == 0 && p[key_size] == '=' && p[key_size + 1] == '"') {
			const char *value = p + key_size + 2;
			const char *close = value;
			while (close < end && *close != '"') {
				close++;
			}
			if (close >= end || close == value) {
				fail("an empty or unterminated attribute ", key, file);
			}
			return copy(value, (size_t)(close - value));
		}
	}
	return NULL;
}

static const char *skip_space(const char *p) {
	while (*p == ' ' || *p == '\t' || *p == '\r' || *p == '\n') {
		p++;
	}
	return p;
}

static const char OPEN[] = "<xlua-chunk ";
static const char CLOSE[] = "</xlua-chunk>";

/* FNV-1a, 32 bits, of the text without carriage returns (LuaEnv.BuiltinChunkHash). */
static unsigned long text_hash(const buffer *text) {
	unsigned long hash = 2166136261UL;
	size_t i;
	for (i = 0; i < text->size; i++) {
		unsigned char c = (unsigned char)text->data[i];
		if (c != '\r') {
			hash = ((hash ^ c) * 16777619UL) & 0xFFFFFFFFUL;
		}
	}
	return hash;
}

static int writer(lua_State *L, const void *p, size_t size, void *ud) {
	(void)L;
	if (p != NULL) {  /* Lua 5.5 ends with a NULL block */
		add((buffer *)ud, p, size);
	}
	return 0;
}

static chunk *chunks = NULL;
static size_t chunk_count = 0;

static void add_chunk(const chunk *c) {
	size_t i;
	chunk *grown;
	for (i = 0; i < chunk_count; i++) {
		if (strcmp(chunks[i].name, c->name) == 0) {
			fprintf(stderr, "xlua_chunkc: chunk %s in %s and in %s\n", c->name, chunks[i].file, c->file);
			exit(1);
		}
	}
	grown = (chunk *)realloc(chunks, (chunk_count + 1) * sizeof(chunk));
	if (grown == NULL) {
		fail("out of memory", NULL, NULL);
	}
	chunks = grown;
	chunks[chunk_count++] = *c;
}

/* Finds the markers of one C# file and keeps the chunks the options select. */
static void scan(const char *file, char **options, int option_count) {
	buffer source = read_file(file);
	const char *p = source.data;
	const char *open;
	if (strlen(source.data) != source.size) {
		fail("a NUL byte", NULL, file);
	}
	while ((open = strstr(p, OPEN)) != NULL) {
		const char *marker_end = strchr(open, '>');
		const char *line_end = strchr(open, '\n');
		const char *literal;
		const char *s;
		const char *close;
		chunk c;
		char *option;
		int wanted = 1;
		int i;
		memset(&c, 0, sizeof(c));
		if (marker_end == NULL || (line_end != NULL && line_end < marker_end)) {
			fail("an unterminated <xlua-chunk> marker", NULL, file);
		}
		c.file = copy(file, strlen(file));
		c.name = attribute(open, marker_end, "name", file);
		c.chunkname = attribute(open, marker_end, "chunkname", file);
		option = attribute(open, marker_end, "option", file);
		if (c.name == NULL || c.chunkname == NULL) {
			fail("a marker without name or chunkname", NULL, file);
		}
		/* The string: the next verbatim literal, before the closing marker. */
		literal = strstr(marker_end, "@\"");
		close = strstr(marker_end, CLOSE);
		if (literal == NULL || close == NULL || close < literal) {
			fail("no verbatim string (@\"...\") before </xlua-chunk> for ", c.name, file);
		}
		if (literal[-1] == '$') {
			fail("an interpolated string for ", c.name, file);
		}
		for (s = literal + 2;; s++) {
			if (*s == '\0') {
				fail("an unterminated string for ", c.name, file);
			}
			if (*s == '"') {
				if (s[1] != '"') {
					break;
				}
				s++;  /* "" is one double quote */
			}
			else if (!((unsigned char)*s >= 0x20 && (unsigned char)*s < 0x7f) && *s != '\t' && *s != '\n' && *s != '\r') {
				fail("a character that is not printable ASCII in ", c.name, file);
			}
			add(&c.text, s, 1);
		}
		if (c.text.data == NULL) {
			add(&c.text, "", 0);
		}
		/* Only ";" and the closing marker may follow, so the chunk is the whole value. */
		s = skip_space(s + 1);
		if (*s != ';') {
			fail("more than one string literal for ", c.name, file);
		}
		s = skip_space(s + 1);
		if (strncmp(s, "//", 2) != 0 || strncmp(skip_space(s + 2), CLOSE, sizeof(CLOSE) - 1) != 0) {
			fail("no </xlua-chunk> right after the string for ", c.name, file);
		}
		p = close + sizeof(CLOSE) - 1;
		if (option != NULL) {
			wanted = 0;
			for (i = 0; i < option_count; i++) {
				if (strcmp(options[i], option) == 0) {
					wanted = 1;
				}
			}
			free(option);
		}
		c.hash = text_hash(&c.text);
		if (wanted) {
			add_chunk(&c);
		}
		else {
			free(c.text.data);
			free(c.name);
			free(c.chunkname);
			free(c.file);
		}
	}
	free(source.data);
}

static void compile(lua_State *L, chunk *c, int strip) {
	if (luaL_loadbufferx(L, c->text.data, c->text.size, c->chunkname, "t") != LUA_OK) {
		fprintf(stderr, "xlua_chunkc: %s (%s): %s\n", c->name, c->file, lua_tostring(L, -1));
		exit(1);
	}
	if (lua_dump(L, writer, &c->code, strip) != 0 || c->code.size == 0) {
		fail("cannot dump ", c->name, c->file);
	}
	lua_pop(L, 1);
}

/* A C string literal of s, which is printable ASCII (a name or chunk name). */
static void put_string(FILE *f, const char *s) {
	fputc('"', f);
	for (; *s; s++) {
		if (*s == '"' || *s == '\\') {
			fputc('\\', f);
		}
		fputc(*s, f);
	}
	fputc('"', f);
}

/* Writes PATH.tmp and renames it, so that a failed run leaves no partial header. */
static void write_header(const char *path) {
	size_t i, j;
	size_t path_size = strlen(path);
	char *temporary = (char *)malloc(path_size + 5);
	FILE *f;
	if (temporary == NULL) {
		fail("out of memory", NULL, NULL);
	}
	memcpy(temporary, path, path_size);
	memcpy(temporary + path_size, ".tmp", 5);
	f = fopen(temporary, "wb");
	if (f == NULL) {
		fail("cannot write ", temporary, NULL);
	}
	fprintf(f, "/* Generated by build/xlua_chunkc.c from the <xlua-chunk> markers of the C#\n"
	           " * runtime: its built-in chunks, compiled for a bytecode-only library (%s).\n"
	           " * The build writes this file again; do not edit it. */\n\n", LUA_RELEASE);
	fprintf(f, "#define XLUA_BUILTIN_CHUNKS_VERSION_NUM %d\n", (int)LUA_VERSION_NUM);
	fprintf(f, "#define XLUA_BUILTIN_CHUNKS_SIZEOF_INT %u\n", (unsigned)sizeof(int));
	fprintf(f, "#define XLUA_BUILTIN_CHUNKS_SIZEOF_SIZE_T %u\n", (unsigned)sizeof(size_t));
	fprintf(f, "#define XLUA_BUILTIN_CHUNKS_SIZEOF_INTEGER %u\n", (unsigned)sizeof(lua_Integer));
	fprintf(f, "#define XLUA_BUILTIN_CHUNKS_SIZEOF_NUMBER %u\n", (unsigned)sizeof(lua_Number));
#if defined(LUAC_COMPATIBLE_FORMAT) && LUAC_COMPATIBLE_FORMAT
	fprintf(f, "#define XLUA_BUILTIN_CHUNKS_COMPATIBLE_FORMAT 1\n");
#else
	fprintf(f, "#define XLUA_BUILTIN_CHUNKS_COMPATIBLE_FORMAT 0\n");
#endif
	for (i = 0; i < chunk_count; i++) {
		const chunk *c = &chunks[i];
		/* A union, so that the bytes are aligned as Lua's own buffers are. */
		fprintf(f, "\n/* %s */\nstatic const union { unsigned char bytes[%lu]; double align; } xlua_builtin_chunk_%lu = {{",
		        c->name, (unsigned long)c->code.size, (unsigned long)i);
		for (j = 0; j < c->code.size; j++) {
			fprintf(f, "%s0x%02x,", j % 16 == 0 ? "\n  " : "", (unsigned)(unsigned char)c->code.data[j]);
		}
		fprintf(f, "\n}};\n");
	}
	fprintf(f, "\nstatic const xlua_builtin_chunk xlua_builtin_chunks[] = {\n");
	for (i = 0; i < chunk_count; i++) {
		const chunk *c = &chunks[i];
		fprintf(f, "  {");
		put_string(f, c->name);
		fprintf(f, ", ");
		put_string(f, c->chunkname);
		fprintf(f, ", 0x%08lxu, xlua_builtin_chunk_%lu.bytes, %lu},\n", c->hash, (unsigned long)i, (unsigned long)c->code.size);
	}
	fprintf(f, "  {NULL, NULL, 0, NULL, 0}\n};\n");
	if (fclose(f) != 0) {
		fail("cannot write ", temporary, NULL);
	}
	remove(path);  /* rename does not replace a file on Windows */
	if (rename(temporary, path) != 0) {
		fail("cannot rename the header to ", path, NULL);
	}
	free(temporary);
}

int main(int argc, char **argv) {
	const char *out = NULL;
	char **options = (char **)calloc((size_t)argc, sizeof(char *));
	int option_count = 0;
	int strip = 0;
	int files = 0;
	int i;
	size_t k;
	lua_State *L;
	if (options == NULL) {
		fail("out of memory", NULL, NULL);
	}
	for (i = 1; i < argc; i++) {
		if (strcmp(argv[i], "-s") == 0) {
			strip = 1;
		}
		else if (strcmp(argv[i], "-D") == 0 && i + 1 < argc) {
			options[option_count++] = argv[++i];
		}
		else if (strcmp(argv[i], "-o") == 0 && i + 1 < argc) {
			out = argv[++i];
		}
		else if (argv[i][0] == '-') {
			fail("unknown option ", argv[i], NULL);
		}
	}
	if (out == NULL) {
		fprintf(stderr, "usage: xlua_chunkc [-s] [-D OPTION]... -o OUT.h FILE.cs...\n");
		return 1;
	}
	for (i = 1; i < argc; i++) {
		if (strcmp(argv[i], "-D") == 0 || strcmp(argv[i], "-o") == 0) {
			i++;
		}
		else if (argv[i][0] != '-') {
			scan(argv[i], options, option_count);
			files++;
		}
	}
	if (files == 0 || chunk_count == 0) {
		fail("no <xlua-chunk> markers found", NULL, NULL);
	}
	L = luaL_newstate();
	if (L == NULL) {
		fail("cannot create a Lua state", NULL, NULL);
	}
	for (k = 0; k < chunk_count; k++) {
		compile(L, &chunks[k], strip);
	}
	lua_close(L);
	write_header(out);
	return 0;
}
