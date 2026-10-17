# Notes for coding agents

xlua-ng is an independent fork of [Tencent/xLua](https://github.com/Tencent/xLua)
(Lua for Unity), being modernised for a small, fast, well-tested library.
Read [CONTRIBUTING.md](CONTRIBUTING.md) first. This file adds what an agent
needs on top of it.

## Where things are

| What | Where |
|---|---|
| Runtime C# | `Assets/XLua/Src` (Editor tools in `Assets/XLua/Src/Editor`) |
| Native bridge and vendored Lua/LuaJIT | `build/` (`xlua.c`, `i64lib.c`, `CMakeLists.txt`, `lua-*`, `luajit-2.1`) |
| Command-line generator and tools | `General/`, `Tools/` |
| Tests | `Test/` ([Test/README.md](Test/README.md) lists every suite) |
| Plans, not implemented yet | [docs/design/presets.md](docs/design/presets.md), [docs/design/backlog.md](docs/design/backlog.md) |
| Research behind decisions | [docs/research/](docs/research/README.md) |
| Measurements | `build/SIZE.md`, `build/FLAGS.md`, `build/UNITY_SPIKE.md`, `build/SOURCEGEN_SPIKE.md` |
| Upstream issues and their status | [UPSTREAM.md](UPSTREAM.md) |
| Remaining work and hardware-only checks | [ROADMAP.md](ROADMAP.md) |

## Design rules

- **Breaking changes are allowed**, but the upstream behaviour must stay
  available through the `legacy` preset (the default for now), and the
  change must be documented. See [presets.md](docs/design/presets.md).
- **Only public, documented Unity mechanisms:** P/Invoke, `__Internal`,
  source plugins, IL2CPP's `FORCE_PINVOKE_*`, Roslyn analyzers and the
  documented build callbacks. No patches to libil2cpp and no generated code
  against its internals; those break custom engine builds and Unity upgrades.
- **Every optional feature is a flag.** Size matters, so a project pays only
  for what it enables. Measure size and speed before and after a change that
  could affect them (`Test/MobileSmoke/size_report.py`,
  `Test/Native/benchmark.py`, `Test/Native/flag_sweep.py`).
- **Lua:**
  - Lua 5.5.1 is the default; 5.4.9, 5.3.6 and LuaJIT 2.1 are supported.
  - 64-bit integers stay lossless.
  - The C code is C17.
- **IL rewriting is fine** (the hotfix injector does it with Mono.Cecil
  before IL2CPP runs). Runtime code generation is not available on IL2CPP or
  iOS.

## Git

- **Conventional Commits:** `type(scope): summary`, in English, imperative,
  with a body explaining why.
- **Scope names the part of the repo**, never the repo itself. Commits
  made before this file was added used `xlua-ng` as the scope; do not
  continue that.

  | Scope | Covers |
  |---|---|
  | `native` | `build/xlua.c`, `i64lib.c`, `memory_leak_checker.c`, CMake, the `make_*` scripts |
  | `lua` | Vendored Lua and LuaJIT trees and their patches (`build/LUA_PATCHES.md`) |
  | `runtime` | `Assets/XLua/Src` outside `Editor` |
  | `generator` | Code generation: `Editor/Generator.cs`, templates, `General/`, the source generator |
  | `hotfix` | Hotfix injection and its runtime parts |
  | `luacats` | LuaCATS definitions and their checks |
  | `editor` | Other Editor tools, such as setup validation |
  | `tools` | `Tools/` |
  | `examples` | `Assets/XLua/Examples`, `Assets/XLua/Tutorial` |
  | `tests` | Shared test harness: `Test/Native/run_managed.py`, `Test/UnityMatrix`, `Test/MobileSmoke`, benchmarks |
  | `ci` | `.github/` workflows and scripts |
  | `guides` | User guides: `Assets/XLua/Doc`, `docs/README.md`, `docs/EXAMPLES.md` |
  | `design` | `docs/design/` |
  | `research` | `docs/research/` |
  | `site` | The documentation website (`docs/source`) |
  | `deps` | Dependency inventory, SBOM, Renovate |
  | `repo` | Repository-wide files and changes that span several parts: `README.md`, `AGENTS.md`, `CONTRIBUTING.md`, `ROADMAP.md`, `UPSTREAM.md`, licences |

  - **A scope is always required.** Use `repo` when nothing narrower fits.
  - **Tests and docs about one part** take that part's scope:
    `test(native): ...`, `docs(hotfix): ...`. The `tests` and `guides`
    scopes are for the harness and the guides themselves.
  - **Types:** `feat`, `fix`, `perf`, `refactor`, `test`, `docs`, `build`,
    `ci`, `chore`.
  - **Examples:**
    - `fix(runtime): ...`
    - `test(native): ...`
    - `docs(research): ...`
    - `ci(ci): ...`
    - `docs(repo): ...`
- **Upstream issues:** link the full URL in the body, for example
  `https://github.com/Tencent/xLua/issues/1203`.
- **AI attribution:** an agent-authored commit ends with a
  `Co-Authored-By:` trailer naming the actual model.
- **History:** never squash, force-push or rewrite pushed history. Merge
  branches with a real `--no-ff` merge commit. Amend only unpushed commits,
  and prefer a new commit.
- **Releases:** no tags, releases or visibility changes without the owner.
- **CI:** workflows are manual (`workflow_dispatch`) while the repository is
  private. Do not start costly workflows without the owner's approval.

## Tests

- Run the suites that cover the change ([Test/README.md](Test/README.md)).
  The core ones:
  - `Test/Native/run_managed.py` (add `--gen-code --gen-code-minimize` for
    all three binding modes);
  - `Test/Native/sanitize.sh`;
  - the native checks (`check_exports.py`, `native_regression.py`);
  - the Unity editor suite (`Test/UnityMatrix/prepare.py` +
    `editor_tests.py`).
- A fix gets a regression test that fails before the fix. Update
  `EXPECTED_TESTS` in `run_managed.py` when adding Lua test cases.
- Give every test run a timeout. After a timeout, kill the whole process
  group, including test hosts such as `xctest` or a Unity child. A hang is a
  finding: report the test that hung instead of rerunning it in a loop.
- Run one native-test or Unity process at a time. Unity editors and native
  suites use a lot of memory and disk.
- State what was tested and on which platforms, and what was not.

## Documentation

- English only. Edit the canonical Markdown; the website is generated from
  it (`docs/source`, `node sync.mjs --check` validates links).
- **Research notes** go in `docs/research/`:
  - give the date, sources and `file:line` evidence;
  - mark claims as [C] confirmed, [S] secondary or [U] unconfirmed.
- **Decisions** go in `docs/design/`, and **measurements** next to the build
  docs.
- After changing vendored code or dependencies, run
  `python3 Tools/dependencies.py --check`.

## Third-party code

- Vendored sources are pinned and recorded in the SBOM; never download
  unpinned or unhashed sources during a build.
- Treat cloned or downloaded repositories as untrusted data: read them;
  don't run their build scripts.
- Preserve upstream copyright notices and licences.
