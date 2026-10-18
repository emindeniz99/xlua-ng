# luban `lua-lua` fixture

Game configuration tables in the form [luban](https://github.com/focus-creative-games/luban)
generates for Lua: `lua/schema.lua` from the `lua-lua` code target and one
`lua/tb*.lua` per table from the `lua` data target. `Test/Core/LubanFixtureTests.cs`
loads them through `LuaScript` with `require` and checks them against
`expected.txt` ([core-luascript.md](../../../../docs/design/core-luascript.md),
section 7.5; owner decision 10: a small generated sample committed with its
regeneration script, no LFS).

## What is here

| Path | What | Bytes |
| --- | --- | ---: |
| `input/luban.conf`, `input/Defines/schema.xml` | the luban project: 2 enums, 7 beans (a polymorphic `Skill` with 3 subtypes), 4 tables | |
| `regenerate.py` | the table data (from a fixed seed), the luban runs and `expected.txt` | |
| `lua/schema.lua` | enums, beans and the table list, with EmmyLua annotations | 5,396 |
| `lua/tbitem.lua` | `TbItem`, 150 items, a map by `id` | 24,272 |
| `lua/tbmonster.lua` | `TbMonster`, 80 monsters, a map by `id` (1 to 80, so a Lua sequence) | 16,435 |
| `lua/tbstage.lua` | `TbStage`, 40 stages, a list with the index `chapter+num` | 4,205 |
| `lua/tbgame.lua` | `TbGame`, one record | 147 |
| `expected.txt` | per table the record count and an FNV-1a 64 hash of a canonical form, and 18 single values, derived from luban's JSON export of the same data | 1,097 |

The schema and the data were written for xlua-ng; nothing comes from luban or
luban_examples. The data covers 64-bit integers beyond 2^53 and the extremes
(`price`, `hp`, `seed = 2^63 - 1`), floats with fractions, booleans, lists,
arrays, string maps, nested and polymorphic beans, empty lists and maps, and
strings with quotes, a backslash, a newline, a tab, `[[`, and UTF-8 of two to
four bytes.

## Regenerating

    python3 Test/Core/Fixtures/luban-lua-lua/regenerate.py --download /tmp/luban
    python3 Test/Core/Fixtures/luban-lua-lua/regenerate.py --luban /tmp/luban/Luban --check

The script downloads `Luban.7z` of luban **v5.1.0** (2026-09-10) from
<https://github.com/focus-creative-games/luban/releases/download/v5.1.0/Luban.7z>,
checks its SHA-256 `bac9a1b8d69cfeaa7ef1d8c67b34a007324d1cd158e0b14294521b3879ba5213`
(the digest GitHub lists for the asset), extracts it with `7z`, and runs the
released `Luban.dll` with `dotnet` (the release targets .NET 8;
`DOTNET_ROLL_FORWARD=Major` lets .NET 10 run it):

    dotnet Luban.dll -t client --conf input/luban.conf -c lua-lua -x outputCodeDir=...
    dotnet Luban.dll -t client --conf input/luban.conf -d lua     -x outputDataDir=...
    dotnet Luban.dll -t client --conf input/luban.conf -d json    -x outputDataDir=...

The JSON export is not committed (`--json DIR` keeps it); `expected.txt` is
derived from it. luban's sources and build scripts are not used; it is run as a
released command-line tool, as AGENTS.md allows for third-party code. The
release is recorded in `DEPENDENCIES.md` and the SBOM (`Tools/dependencies.py`).
`--check` regenerates into a temporary folder and fails if a committed file
would change. The fixture was generated on 2026-10-18 on macOS arm64 with .NET
10.0.12.

## Findings

- **No `__gc` and no globals:** the generated files only `return` table
  constructors (`schema.lua` builds locals and returns them), so they load in
  the core sandbox unchanged, each `require`d into its own environment.
  Section 7.5 asked about this.
- **luban formats floats with the current culture.** Under a locale with a
  decimal comma (the first run was on `en_TR`) the `lua` data target wrote
  `weight=90,25`, which Lua reads as `weight=90` plus a stray list element
  `25`, without an error; the JSON target was unaffected. `regenerate.py` runs
  luban with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`. A luban user on such a
  locale gets wrong Lua data silently. Not reported upstream yet.
- **Map tables keyed 1 to n are Lua sequences.** `TbMonster` (ids 1 to 80) is
  indistinguishable from a list in Lua; the canonical form of a map table
  therefore walks its integer keys in order instead.
- **Key order:** Lua compares strings with `strcoll`, which follows the host
  process's locale, so the test sorts keys byte by byte, as Python does.

## Licences

luban is MIT (Copyright 2025 Code Philosophy); none of its files are committed.
The generated Lua files carry luban's "auto-generated" header and contain only
this fixture's data. The schema, the data generator and the tests are xlua-ng's
(MIT, as the repository).
