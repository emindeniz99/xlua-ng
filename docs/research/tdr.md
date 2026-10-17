# Tencent TDR (`Resources/tdr/tdr.lua.txt`)

Researched 2026-10-17. Confidence: **[C]** confirmed from a primary source,
**[S]** secondary, **[U]** unconfirmed.

## What ships in xLua

`Assets/XLua/Resources/tdr/tdr.lua.txt` is a thin wrapper that has not changed
since upstream's first commit (2016-12-20). It does `require "libtdrlua"` and
exposes:

- `from_file(file)` and `from_memory(str)`. Each loads a compiled "metalib"
  and returns a table indexed lazily by message name.
- `lib.<Message>` returns `{pack(obj) -> string, unpack(str) -> table}`.
  `unpack` always passes cut version 0, meaning the current version.

The native module `libtdrlua` was never published. No `tdrlua` or `libtdr*`
path appears anywhere in the xLua history. The only trace is an include
directory (`/I..\..\tdrlua`) in an old LuaJIT build script that bafd9cc
removed. `Test/UnitTest/StreamingAssets/luaTdrTest.lua` is disabled in
`main.lua`. Its test data, `testxxx.tdr` and `testxxx2.tdr`, are compiled
binary metalibs; their XML source is not in the repository. **[C]**

Upstream added TDR in v2.0.0 and disabled loading it by default in v2.1.0. In
[Tencent/xLua#87](https://github.com/Tencent/xLua/issues/87) a maintainer
answered that the TDR-enabled build is internal, available from Tencent's own
code platform. The module was always for Tencent teams. **[C]**

## What TDR is

TDR (Tencent Data Representation) is Tencent's data description language for
game-server protocols. It came from TSF4G and is still one of the two
table-definition languages of TcaplusDB, the other being Protobuf
([docs](https://tencentcloud.com/document/product/1016/36560)). **[C]**

There is no licensed public source for the C runtime, the `tdr` compiler or
`libtdrlua`. The closest public code is
[tencentyun/tsf4g](https://github.com/tencentyun/tsf4g), which has a Python/Go
code generator and a Go runtime but no LICENSE file.
[TencentOpen/TSF4G-DR](https://github.com/TencentOpen/TSF4G-DR) is an empty
repository. **[C]**

### Schema (XML metalib)

```xml
<metalib name="demo" tagsetversion="1" version="2">
  <macro name="MAX_NAME" value="16"/>
  <struct name="Item" version="1">
    <entry name="id"  type="uint"/>
    <entry name="qty" type="ushort"/>
  </struct>
  <struct name="Bag" version="1" versionindicator="ver">
    <entry name="ver"   type="ushort"/>
    <entry name="name"  type="string" size="MAX_NAME"/>
    <entry name="n"     type="int"/>
    <entry name="items" type="Item" count="8" refer="n"/>  <!-- length lives in n -->
    <entry name="extra" type="int" version="2"/>           <!-- added in version 2 -->
  </struct>
</metalib>
```

- `struct`, `union`, `macro` and `macrosgroup` are the top-level elements.
- `entry` has these attributes:
  - `type`: a built-in or a custom type;
  - `size`: maximum string size;
  - `count`: array capacity;
  - `refer`: the earlier field that holds the actual length;
  - `select`: for unions, the field that chooses the member;
  - `version`: the metalib version that added the field;
  - `defaultvalue`.
- Built-in types are `int8` to `int64`, `uint8` to `uint64`, `float`,
  `double`, `char`, `string`, `wchar`, `wstring`, `date`, `time`, `datetime`
  and `ip`.

### Wire format

The rules below were derived from the Python and Go ports in
`tencentyun/tsf4g`. The C library was not inspected. **[S/U]**

- **Byte order and layout:** big-endian. Fields have fixed widths, with no
  tags, names or padding.
- **Strings:** a 4-byte length that includes the trailing NUL, then the bytes,
  then the NUL.
- **Arrays:** no length prefix. The count comes from the `refer` field.
- **Unions:** no tag. The selector is an earlier field of the parent.
- **Versioning:** a field is written only if the target version is at least
  the field's `version`. Newer code reads older data; the reverse does not
  work.

`Bag{ver=2, name="hi", n=1, items={{id=7, qty=3}}, extra=9}` packs to 23 bytes:

```
00 02 | 00 00 00 03 68 69 00 | 00 00 00 01 | 00 00 00 07 00 03 | 00 00 00 09
 ver     len=3  "hi" NUL         n=1           id=7       qty=3     extra=9
```

## Alternatives for a Lua user today

| Option | Use |
|---|---|
| `string.pack` / `string.unpack` (Lua 5.3+, built in) | Simple binary layouts with no dependency. Covers most of what the TDR wrapper did. LuaJIT has no `string.pack`; it has `string.buffer` instead. |
| [lua-protobuf](https://github.com/starwing/lua-protobuf) | Versioned, schema-driven messages compatible with the Protobuf ecosystem. This is the modern counterpart of TDR. |
| [sproto](https://github.com/cloudwu/sproto) | Small schema language, popular in Chinese game servers. |
| [lua-rapidjson](https://github.com/xpol/lua-rapidjson), [lua-cjson](https://github.com/openresty/lua-cjson) | JSON. Used as the example in [Add_Remove_Lua_Lib.md](../../Assets/XLua/Doc/Add_Remove_Lua_Lib.md). |

## Decision

Remove `tdr` and record the removal in the changelog. Add a dependency-free
`string.pack` example that packs and unpacks a versioned message, and point to
lua-protobuf for schema-based protocols.

## Removal (2026-10-17)

Done: `Resources/tdr/tdr.lua.txt`, the disabled `luaTdrTest.lua` suite, the
binary metalibs `testxxx.tdr`/`testxxx2.tdr` (in `StreamingAssets` and as
`.tdr.bytes` in `Test/UnitTest/xLuaTest/Resources`) and
`LuaTestCommon.xxxtdrfilepath` were deleted. The `string.pack` example is in
[ECOSYSTEM.md](../ECOSYSTEM.md#stringpack-and-stringunpack) and runs in the
suite as `CaseStringPackVersionedMessage`.

Correction: the XML metalib sources were in the repository after all.
`Test/UnitTest/xLuaTest/Resources/non_tlv_net_protocol.xml`,
`non_tlv_net_protocol_v4.xml` and `non_tlv_net_msg_type.xml` (deleted in the
same change) are TDR metalibs with `struct`, `entry`, `version` and
`defaultvalue` attributes, such as `PkgHead` with `datetime` added in version 2
and `srcIp` in version 3. Nothing in the repository compiled them. **[C]**
