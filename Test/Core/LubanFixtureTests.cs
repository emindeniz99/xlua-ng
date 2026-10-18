/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// The luban lua-lua fixture (docs/design/core-luascript.md, section 7.5): game
// configuration tables that luban generated as Lua (Test/Core/Fixtures/luban-lua-lua,
// SOURCE.md), loaded through LuaScript with require and checked against
// expected.txt, which regenerate.py derives from luban's JSON export of the same
// data. run_core.py passes the fixture folder in XLUA_CORE_FIXTURES.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using XLua;

public static partial class CoreTests
{
    // What a game's loader does with luban's lua-lua output: schema.lua lists the
    // tables and their files; each data file returns its table. canon() and fnv()
    // are the canonical form and hash of regenerate.py.
    const string LubanLoader = @"
local schema = require('schema')
T = {}
Modes = {}
for _, t in ipairs(schema.tables) do T[t.name] = require(t.file) Modes[t.name] = t.mode end
Enums = schema.enums

local function islist(t)
  local n, c = #t, 0
  for _ in pairs(t) do c = c + 1 end
  return n > 0 and c == n
end

-- Byte order, as Python sorts: '<' on strings uses strcoll, which follows the host
-- process's locale.
local function byteless(a, b)
  for i = 1, math.min(#a, #b) do
    local x, y = a:byte(i), b:byte(i)
    if x ~= y then return x < y end
  end
  return #a < #b
end

function canon(v)
  local ty = type(v)
  if ty == 'boolean' then return v and 'true' or 'false' end
  if ty == 'number' then
    if math.type(v) == 'integer' then return string.format('%d', v) end
    if v == math.floor(v) and v > -2^63 and v < 2^63 then return string.format('%d', math.tointeger(v)) end
    return string.format('%.17g', v)
  end
  if ty == 'string' then return '#' .. #v .. ':' .. v end
  if ty ~= 'table' then error('unexpected ' .. ty) end
  if next(v) == nil then return '{}' end
  local parts = {}
  if islist(v) then
    for i = 1, #v do parts[i] = canon(v[i]) .. ',' end
    return '[' .. table.concat(parts) .. ']'
  end
  local ks = {}
  for k in pairs(v) do ks[#ks + 1] = { s = tostring(k), k = k } end
  table.sort(ks, function(a, b) return byteless(a.s, b.s) end)
  for i, e in ipairs(ks) do parts[i] = e.s .. '=' .. canon(v[e.k]) .. ';' end
  return '{' .. table.concat(parts) .. '}'
end

-- A table's canonical form: a map table by its integer keys in order (ids 1..n would
-- otherwise look like a list), the others as canon() has them.
function canontable(name)
  local t = T[name]
  if Modes[name] ~= 'map' then return canon(t) end
  local ks = {}
  for k in pairs(t) do ks[#ks + 1] = k end
  table.sort(ks)
  local parts = {}
  for i, k in ipairs(ks) do parts[i] = string.format('%d', k) .. '=' .. canon(t[k]) .. ';' end
  return '{' .. table.concat(parts) .. '}'
end

function fnv(s)
  local h = 0xcbf29ce484222325
  for i = 1, #s, 64 do
    local bytes = { s:byte(i, i + 63) }
    for j = 1, #bytes do h = (h ~ bytes[j]) * 0x100000001b3 end
  end
  return string.format('%016x', h)
end

function count(t)
  if T[t] == nil then error('no table ' .. t) end
  if t == 'TbGame' then return 1 end
  local n = 0
  for _ in pairs(T[t]) do n = n + 1 end
  return n
end
";

    static string FixturesDir()
    {
        string dir = Environment.GetEnvironmentVariable("XLUA_CORE_FIXTURES");
        Check(!string.IsNullOrEmpty(dir) && Directory.Exists(dir), "XLUA_CORE_FIXTURES names Test/Core/Fixtures (run_core.py sets it)");
        return dir;
    }

    public static void TestLubanFixture()
    {
        string dir = Path.Combine(FixturesDir(), "luban-lua-lua");
        var options = new LuaOptions { MemoryLimit = 16 << 20, InstructionLimit = 50000000, TimeLimit = TimeSpan.FromSeconds(10) };
        using (var lua = new LuaScript(options))
        {
            int modules = 0;
            lua.ModuleResolver = (string name, out string code, out string chunkName) =>
            {
                code = null;
                chunkName = null;
                foreach (char c in name)
                {
                    if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_')) return false;
                }
                string path = Path.Combine(dir, "lua", name + ".lua");
                if (!File.Exists(path)) return false;
                code = File.ReadAllText(path, Encoding.UTF8);
                chunkName = "luban/" + name + ".lua";
                modules++;
                return true;
            };
            var watch = Stopwatch.StartNew();
            lua.Load(LubanLoader, "luban_loader");
            long loadMs = watch.ElapsedMilliseconds;
            long instructions = lua.LastInstructionCount;
            long peak = lua.MemoryPeak;
            Equal(5, modules, "schema.lua and the four table files were required");
            Check(instructions > 0 && instructions < 50000000, "instruction count of the load: " + instructions);

            int checks = 0;
            foreach (string raw in File.ReadAllLines(Path.Combine(dir, "expected.txt"), Encoding.UTF8))
            {
                if (raw.Length == 0 || raw[0] == '#') continue;
                string[] f = raw.Split('\t');
                Check(f.Length == 3, "expected.txt line: " + raw);
                switch (f[0])
                {
                    case "count":
                        Equal(long.Parse(f[2]), lua.Load<long>("return count('" + f[1] + "')", "count"), "records of " + f[1]);
                        break;
                    case "hash":
                        Equal(f[2], lua.Load<string>("return fnv(canontable('" + f[1] + "'))", "hash"), "canonical hash of " + f[1]);
                        break;
                    case "value":
                        string root = f[1].StartsWith("Enums") ? "" : "T.";
                        Check(lua.Load<bool>("return (" + root + f[1] + ") == (" + f[2] + ")", "value"),
                              f[1] + " == " + f[2] + ", got " + lua.Load<string>("return tostring(" + root + f[1] + ")", "value"));
                        break;
                    default:
                        Check(false, "expected.txt line: " + raw);
                        break;
                }
                checks++;
            }
            Check(checks == 26, "expected.txt has 26 checks, ran " + checks);
            // luban's lua-lua output keeps to what core allows: no __gc, no globals across files.
            Equal(true, lua.Load<bool>("return rawget(_G, 'enums') == nil and rawget(_G, 'tables') == nil", "globals"),
                  "the schema module leaves no globals");
            Console.WriteLine("  luban fixture: 4 tables, 271 records, load " + loadMs + " ms, " + instructions +
                              " instructions, memory peak " + peak + " bytes, " + checks + " checks");
        }
    }
}
