#!/usr/bin/env python3
"""Regenerate the luban `lua-lua` fixture of LuaScript (docs/design/core-luascript.md, section 7.5).

    python3 Test/Core/Fixtures/luban-lua-lua/regenerate.py --download DIR     fetch the pinned release into DIR
    python3 Test/Core/Fixtures/luban-lua-lua/regenerate.py --luban DIR/Luban  use an extracted release
    python3 Test/Core/Fixtures/luban-lua-lua/regenerate.py --luban ... --check  regenerate into a temporary
                                                                               folder and compare

The input is this folder's input/ (luban.conf and Defines/schema.xml, written for xlua-ng) and table
data that this script generates from a fixed seed (Data/*.json, not committed). The pinned luban
release (LUBAN_URL, checked against LUBAN_SHA256; luban is MIT) runs twice with the .NET runtime:

    dotnet Luban.dll -t client -c lua-lua -d lua --conf luban.conf  ->  lua/schema.lua, lua/tb*.lua
    dotnet Luban.dll -t client -d json --conf luban.conf            ->  the JSON export (not committed)

From the JSON export the script writes expected.txt, which Test/Core/LubanFixtureTests.cs checks
against the Lua data loaded through LuaScript: the record count and an FNV-1a 64 hash of a canonical
form of every table (canon() below and in the test), and a few single values. The release is run as
a command-line tool only; its sources and build scripts are not used. 7z (p7zip) extracts the
release. The output is deterministic: the same release and seed give the same files.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import urllib.request

LUBAN_VERSION = "v5.1.0"
LUBAN_URL = "https://github.com/focus-creative-games/luban/releases/download/v5.1.0/Luban.7z"
LUBAN_SHA256 = "bac9a1b8d69cfeaa7ef1d8c67b34a007324d1cd158e0b14294521b3879ba5213"
SEED = 20261018
here = Path(__file__).resolve().parent


class Rng:
    """A 64-bit LCG, so the data does not depend on Python's random module."""
    def __init__(self, seed):
        self.x = seed

    def next(self):
        self.x = (self.x * 6364136223846793005 + 1442695040888963407) % (1 << 64)
        return self.x >> 33

    def int(self, lo, hi):
        return lo + self.next() % (hi - lo + 1)

    def pick(self, items):
        return items[self.next() % len(items)]


ADJECTIVES = ["Iron", "Silver", "Ancient", "Swift", "Cursed", "Royal", "Frozen", "Burning", "Silent", "Golden"]
NOUNS = ["Sword", "Shield", "Bow", "Helm", "Potion", "Ring", "Axe", "Staff", "Ore", "Herb"]
TAGS = ["melee", "ranged", "magic", "rare", "quest", "craft", "tradeable"]
STATS = ["atk", "def", "spd", "crit", "hp"]
ELEMENTS = ["fire", "ice", "storm", "earth", "void"]
# Strings that luban must escape for Lua: quotes, a backslash, a newline, a tab, and UTF-8 of two
# to four bytes.
SPECIAL = ['He said "hi"', "back\\slash", "line1\nline2", "tab\there", "Çğüşİö 魔剑 \U0001F5E1", "[[not a long string]]"]


def data(rng):
    items, monsters, stages = [], [], []
    for n in range(150):
        iid = 1001 + n
        price = rng.int(1, 1000000)
        if n % 50 == 7:
            price = (1 << 53) + n  # not exact as a double
        if n == 149:
            price = -(1 << 62) - 3
        items.append({
            "id": iid,
            "name": "%s %s" % (rng.pick(ADJECTIVES), rng.pick(NOUNS)),
            "desc": SPECIAL[n] if n < len(SPECIAL) else "item %d" % iid,
            "kind": ["WEAPON", "ARMOR", "POTION", "MATERIAL"][n % 4],
            "quality": ["WHITE", "GREEN", "BLUE", "PURPLE", "ORANGE"][rng.int(0, 4)],
            "price": price,
            "weight": rng.int(0, 400) / 4,
            "stackable": n % 3 == 0,
            "tags": [rng.pick(TAGS) for _ in range(rng.int(0, 3))],
            "attrs": sorted({rng.pick(STATS): rng.int(-20, 200) for _ in range(rng.int(0, 3))}.items()),
        })
    for n in range(80):
        skills = []
        for k in range(rng.int(1, 3)):
            sid = (n + 1) * 10 + k
            kind = rng.int(0, 2)
            if kind == 0:
                skills.append({"$type": "Damage", "id": sid, "power": rng.int(4, 400) / 4, "element": rng.pick(ELEMENTS)})
            elif kind == 1:
                skills.append({"$type": "Heal", "id": sid, "amount": rng.int(10, 500)})
            else:
                skills.append({"$type": "Buff", "id": sid, "stat": rng.pick(STATS), "percent": rng.int(1, 100) / 8,
                               "seconds": rng.int(1, 60)})
        monsters.append({
            "id": 1 + n,
            "name": "monster %d" % (1 + n),
            "level": rng.int(1, 99),
            "hp": rng.int(100, 100000) * (100000 if n % 10 == 9 else 1),
            "boss": n % 10 == 9,
            "skills": skills,
            "drops": [{"itemId": 1001 + rng.int(0, 149), "count": rng.int(1, 5)} for _ in range(rng.int(0, 3))],
        })
    for chapter in range(1, 5):
        for stage in range(1, 11):
            stages.append({
                "chapter": chapter, "num": stage, "name": "%d-%d" % (chapter, stage),
                "monsters": [rng.int(1, 80) for _ in range(rng.int(1, 4))],
                "stars": [rng.int(10, 99) * 100 for _ in range(3)],
                "reward": {"itemId": 1001 + rng.int(0, 149), "count": rng.int(1, 10)},
            })
    game = {"version": 7, "title": "xlua-ng luban fixture", "maxLevel": 99, "seed": (1 << 63) - 1,
            "startItems": [{"itemId": 1001, "count": 1}, {"itemId": 1002, "count": 2}]}
    return {"item.json": items, "monster.json": monsters, "stage.json": stages, "game.json": game}


# ---- the canonical form and hash; LubanFixtureTests.cs has the same in Lua ----

def canon(v):
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, int):
        return "%d" % v
    if isinstance(v, float):
        return "%d" % int(v) if v.is_integer() and abs(v) < 2 ** 63 else "%.17g" % v
    if isinstance(v, str):
        b = v.encode("utf-8")
        return "#%d:" % len(b) + v
    if isinstance(v, list):
        return "{}" if not v else "[" + "".join(canon(x) + "," for x in v) + "]"
    if isinstance(v, dict):
        return "{}" if not v else "{" + "".join(k + "=" + canon(v[k]) + ";" for k in sorted(v)) + "}"
    raise TypeError(type(v))


def fnv1a64(text):
    h = 0xcbf29ce484222325
    for b in text.encode("utf-8"):
        h = ((h ^ b) * 0x100000001b3) & 0xFFFFFFFFFFFFFFFF
    return "%016x" % h


def lua_literal(v):
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, (int, float)):
        return canon(v)
    return '"' + "".join(chr(b) if 32 <= b < 127 and b not in (34, 92) else "\\%03d" % b for b in v.encode("utf-8")) + '"'


def normalize(v, schema_types=True):
    """The JSON export as the Lua data has it: "$type" is "_type_", maps (lists of pairs) are tables."""
    if isinstance(v, dict):
        return {("_type_" if k == "$type" else k): normalize(x) for k, x in v.items()}
    if isinstance(v, list):
        if v and all(isinstance(x, list) and len(x) == 2 and isinstance(x[0], str) for x in v):
            return {k: normalize(x) for k, x in v}
        return [normalize(x) for x in v]
    return v


def expected(json_dir):
    tables = {"TbItem": ("tbitem.json", "id"), "TbMonster": ("tbmonster.json", "id"),
              "TbStage": ("tbstage.json", None), "TbGame": ("tbgame.json", "one")}
    lines = ["# Generated by regenerate.py from luban's JSON export of the same data; do not edit.",
             "# count TABLE N | hash TABLE FNV-1a-64(canonical form of TABLE) | value LUA-PATH LUA-LITERAL"]
    for name, (file, key) in tables.items():
        rows = [normalize(r) for r in json.loads((json_dir / file).read_text(encoding="utf-8"))]
        if key == "one":
            text, count = canon(rows[0]), 1
        elif key:
            # A map table by its integer keys in order (Lua sees ids 1..n as a list, so canon() alone
            # would not do).
            text = "{" + "".join("%d=%s;" % (r[key], canon(r)) for r in sorted(rows, key=lambda r: r[key])) + "}"
            count = len(rows)
        else:
            text, count = canon(rows), len(rows)
        lines.append("count\t%s\t%d" % (name, count))
        lines.append("hash\t%s\t%s" % (name, fnv1a64(text)))
    items = {r["id"]: normalize(r) for r in json.loads((json_dir / "tbitem.json").read_text(encoding="utf-8"))}
    monsters = {r["id"]: normalize(r) for r in json.loads((json_dir / "tbmonster.json").read_text(encoding="utf-8"))}
    stages = [normalize(r) for r in json.loads((json_dir / "tbstage.json").read_text(encoding="utf-8"))]
    game = normalize(json.loads((json_dir / "tbgame.json").read_text(encoding="utf-8"))[0])
    samples = [("TbItem[1001].desc", items[1001]["desc"]), ("TbItem[1003].desc", items[1003]["desc"]),
               ("TbItem[1005].desc", items[1005]["desc"]), ("TbItem[1008].price", items[1008]["price"]),
               ("TbItem[1150].price", items[1150]["price"]), ("TbItem[1042].name", items[1042]["name"]),
               ("TbItem[1042].quality", items[1042]["quality"]), ("TbItem[1042].kind", items[1042]["kind"]),
               ("TbItem[1042].weight", items[1042]["weight"]), ("TbMonster[10].boss", monsters[10]["boss"]),
               ("TbMonster[10].hp", monsters[10]["hp"]), ("TbMonster[3].skills[1]._type_", monsters[3]["skills"][0]["_type_"]),
               ("TbStage[13].name", stages[12]["name"]), ("TbStage[13].stars[3]", stages[12]["stars"][2]),
               ("TbGame.seed", game["seed"]), ("TbGame.title", game["title"]),
               ("Enums['Quality'].ORANGE", 4), ("Enums['ItemKind'].POTION", 3)]
    lines += ["value\t%s\t%s" % (path, lua_literal(v)) for path, v in samples]
    return "\n".join(lines) + "\n"


def download(target):
    target.mkdir(parents=True, exist_ok=True)
    archive = target / "Luban.7z"
    if not archive.is_file():
        print("downloading", LUBAN_URL)
        with urllib.request.urlopen(LUBAN_URL) as r, open(archive, "wb") as f:
            shutil.copyfileobj(r, f)
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    if digest != LUBAN_SHA256:
        sys.exit("Luban.7z: sha256 %s, expected %s" % (digest, LUBAN_SHA256))
    if not (target / "Luban/Luban.dll").is_file():
        subprocess.run(["7z", "x", "-y", "-o" + str(target), str(archive)], check=True, stdout=subprocess.DEVNULL)
    return target / "Luban"


def run_luban(dotnet, luban, conf, args):
    # The release targets .NET 8; roll forward to a newer installed runtime. luban formats floats with
    # the current culture: under a locale with a decimal comma (tr_TR, de_DE) lua-lua data gets
    # "weight=90,25", which Lua reads as weight=90 and a stray list element. The invariant culture
    # gives "90.25" everywhere.
    env = dict(os.environ, DOTNET_ROLL_FORWARD="Major", DOTNET_CLI_TELEMETRY_OPTOUT="1",
               DOTNET_SYSTEM_GLOBALIZATION_INVARIANT="1", LANG="C", LC_ALL="C")
    cmd = [dotnet, str(luban / "Luban.dll"), "-t", "client", "--conf", str(conf)] + args
    print("+", " ".join(cmd), flush=True)
    r = subprocess.run(cmd, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, timeout=600)
    if r.returncode != 0:
        print(r.stdout)
        sys.exit("luban failed")


def generate(dotnet, luban, out, keep_json=None):
    with tempfile.TemporaryDirectory() as tmp:
        work = Path(tmp)
        shutil.copytree(here / "input", work / "input")
        (work / "input/Data").mkdir()
        for name, rows in data(Rng(SEED)).items():
            (work / "input/Data" / name).write_text(json.dumps(rows, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
        conf = work / "input/luban.conf"
        run_luban(dotnet, luban, conf, ["-c", "lua-lua", "-x", "outputCodeDir=" + str(work / "code")])
        run_luban(dotnet, luban, conf, ["-d", "lua", "-x", "outputDataDir=" + str(work / "lua")])
        run_luban(dotnet, luban, conf, ["-d", "json", "-x", "outputDataDir=" + str(work / "json")])
        lua = out / "lua"
        shutil.rmtree(lua, ignore_errors=True)
        lua.mkdir(parents=True)
        for f in sorted((work / "code").glob("*.lua")) + sorted((work / "lua").glob("*.lua")):
            shutil.copy2(f, lua / f.name)
        (out / "expected.txt").write_text(expected(work / "json"), encoding="utf-8")
        if keep_json:
            shutil.rmtree(keep_json, ignore_errors=True)
            shutil.copytree(work / "json", keep_json)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    g = p.add_mutually_exclusive_group(required=True)
    g.add_argument("--luban", type=Path, help="an extracted release (the folder with Luban.dll)")
    g.add_argument("--download", type=Path, metavar="DIR", help="download and extract the pinned release into DIR")
    p.add_argument("--dotnet", default=shutil.which("dotnet") or "dotnet")
    p.add_argument("--json", type=Path, metavar="DIR", help="also keep luban's JSON export in DIR (not committed)")
    p.add_argument("--check", action="store_true", help="generate into a temporary folder and compare with this one")
    a = p.parse_args()
    luban = download(a.download.resolve()) if a.download else a.luban.resolve()
    if a.check:
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp)
            generate(a.dotnet, luban, out)
            names = sorted(f.relative_to(out).as_posix() for f in out.rglob("*") if f.is_file())
            committed = sorted(f.relative_to(here).as_posix() for f in [here / "expected.txt"] + list((here / "lua").glob("*")))
            diff = [n for n in names if not (here / n).is_file() or (here / n).read_bytes() != (out / n).read_bytes()]
            if diff or names != committed:
                sys.exit("LUBAN_FIXTURE_DIFFERS: " + ", ".join(diff or sorted(set(names) ^ set(committed))))
        print("LUBAN_FIXTURE_UNCHANGED")
        return
    generate(a.dotnet, luban, here, a.json)
    for f in sorted([here / "expected.txt"] + list((here / "lua").glob("*"))):
        print("%s %7d %s" % (hashlib.sha256(f.read_bytes()).hexdigest()[:16], f.stat().st_size, f.relative_to(here)))


if __name__ == "__main__":
    main()
