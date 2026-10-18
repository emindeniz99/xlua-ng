# Pre-public audit (2026-10-18)

An audit of `emindeniz99/xlua-ng` before the owner decides whether to make
the repository public. It covers secrets, private data in files and commit
messages, history size, licences, the workflows and repository settings,
and a short security pass over the native bridge and the C# runtime. It
changed nothing on GitHub: no push, no settings, no history rewrite.

Scope: `origin/main` at `df205401` (the only ref on GitHub; the 24 upstream
tags, 93 local branches and 4 `refs/agents/*` checkpoint refs exist only in
the owner's clone). Secret scans also covered every local ref.

Confidence marks: [C] confirmed here by a scan, a command or the code at the
cited line, [S] from a secondary source, [U] unconfirmed. Severity: HIGH
blocks the flip, MED should be settled before the flip, LOW can wait.
Sensitive values are redacted; the raw scan reports are kept outside the
repository with the other work files.

## Verdict

Nothing found blocks the flip: no secret, no credential, no licence gap [C].
The code pass found two HIGH defects in the unreleased core sandbox
(section 6); both were reproduced and fixed the same day (see the update in
section 6). Six items
need the owner before the flip (see [Owner questions](#owner-questions)),
the main one being whether the owner's personal email may stay in the
author field of 1,043 commits; changing that needs a history rewrite,
which is only cheap before the flip.

## Tools

All four were already installed through Homebrew; nothing was downloaded.
Homebrew builds its bottles itself, so their hashes cannot be checked
against the projects' GitHub release checksums; the hashes below identify
the binaries that ran.

| Tool | Version | SHA-256 of the binary |
|---|---|---|
| gitleaks | 8.30.1 | `f414bc2fb952be6c9072b75cb411e3368614ef4b16d48dbd9ad238034afd2302` |
| trufflehog | 3.99.2 | `95de8fd67df202340d333b01f80076791d2d98cd2d367bf37f8b8e2e3aa2363b` |
| zizmor | 1.31.1 | `b9351fa362c90bf7adb1dec159f2c3dc03d6985a1091f7d8df243de2c9446ec2` |
| actionlint | 1.7.12 | `0bb6a3ba245f823147f0580dd72679dfa7c389262ffdbb88c749fb808a1db25f` |

trufflehog ran with `--no-verification`, so it made no network requests
to check candidate secrets against live services. An `--only-verified` run
is meaningless without verification (it reports nothing by construction),
so it was not counted as a separate result. The private-data sweep is a
Python script over every blob reachable from any ref (`git rev-list
--objects --all` and `git cat-file --batch`) and every commit message
(`git log --all`), with per-category regular expressions.

## 1. Secrets

| Scan | Scope | Result |
|---|---|---|
| gitleaks `detect --log-opts=--all` | 1,736 commits, 48 MB | 1 finding, false positive |
| gitleaks `detect --no-git` | worktree, 16 MB | none |
| trufflehog `git file://…` (no verification) | 24,631 chunks, 114 MB | none |
| trufflehog `filesystem` | worktree | none |
| gitleaks and trufflehog | the UPM tarball from `Tools/make_upm_package.py --tarball` (`com.xlua-ng.xlua` 0.0.1-dev, 432 files, SHA-256 `c37a79d4…f651ef9`) | none |

- **LOW, false positive** [C]: gitleaks `private-key` in
  `General/Src/KeyPairsGen.cs:34` at upstream commit `ed1e7240` (2017,
  Tencent). The match is the PEM header string literal the key generator
  writes, not a key.
- **LOW, published demo key** [C]: `Assets/XLua/Examples/10_SignatureLoader/otherfiles/key_ras`
  is an RSA private key in .NET XML form (`<RSAKeyValue>` with `<D>`). Neither
  engine detects that format; a manual search for `<RSAKeyValue>` with a
  private exponent over every blob found only this file and its earlier
  upstream versions. `Assets/XLua/Doc/signature.md:28` documents it as a
  published demo key that must never sign production scripts. It also ships
  in the UPM package under `Samples~/Examples`. Acceptable as a documented
  fixture; see owner question 5.
- No `.env`, keystore, provisioning profile, Unity licence file or token was
  ever committed [C]. The repository has no Actions secrets yet, no deploy
  keys and no webhooks [C] (`gh api`).

## 2. Private data

Counts are distinct blobs (file versions) reachable from `origin/main`, and
commits on `origin/main` for messages.

| Category | In the current tree | Only in history | Commit messages |
|---|---|---|---|
| Owner's email `e…@gmail.com` in file contents | 0 | 0 | 0 |
| Owner's email as git author/committer | n/a | 1,043 commits | n/a |
| `/Users/<owner>`, `/private/tmp/claude-…`, `/var/folders/…` | 0 | 0 | 0 |
| External work disk path (`/Volumes/T7…`, `ext-ai-projects`) | 15 files before this audit, 6 after (code defaults and one README line) | 183 older blobs of the same files | 1 (`b14ab72d`, "Dependencies remain on T7…") |
| Machine name (`MacBook-Pro-…`) | 0 | 0 | 0 |
| Private IPs, adb ports, phone serials, mDNS names | 0 | 1 upstream XML fixture with a `10.x` default value (Tencent, removed) | 0 |
| Phone model ("moto g play 2024") | 7 files | older blobs | 8 |
| Apple team id, Unity serial or licence content, AWS ids, tokens | 0 | 0 | 0 |
| Citations of the private playground repository and its phone log (`CHANGES.md`) | 2 files before this audit, 0 after | older blobs of the same 2 files | 0 |
| `morning.md`, `MEMORY.md`, `.claude/rules`, other private documents | 0 | 0 | 0 |
| Claude Code worktree branch names (`worktree-agent-<hex>`) | 0 | 0 | 106 merge subjects |
| Agent session UUIDs | 0 | 0 | 7, only on local `refs/agents/*` checkpoint commits, not on `origin/main` |

Findings:

- **MED, owner's email in commit metadata** [C]: 1,043 commits after the
  fork point `59bf4268` carry the owner's personal Gmail address as author
  and committer. GitHub shows it to anyone (for example through a commit's
  `.patch` URL) once the repository is public. Upstream contributors'
  addresses (Tencent, qq.com and others) are already public in
  Tencent/xLua and need nothing. Options: accept it, or rewrite the 1,043
  commits with a mailmap to the GitHub `noreply` address before the flip
  (cost in [History rewrite](#history-rewrite)), and set `user.email` to the
  `noreply` address for future commits either way.
- **LOW, work disk paths** [C]: fixed in the docs by this audit (commit
  `docs(repo): drop local work paths…`). Left for the owner because they are
  defaults of the owner's tools rather than prose:
  `Test/Native/editor_compile_check.py:57` and `webgl_compile_check.py:15,28`
  (editor discovery lists, harmless on other machines),
  `Test/Native/migration_check.py:55` (default `--unity`),
  `Test/Native/flag_sweep.py:1108` (default `--lock`),
  `Tools/SourceGenSpike/{build_generator.sh,locked_run.sh,run_spike.py,summarize.py}`
  (spike scripts with a fixed `BASE`), and `Test/README.md:1825`. They reveal
  a volume name and nothing else. The history copies need no rewrite.
- **LOW, private document citations** [C]: `Test/MobileSmoke/README.md`
  (two places) and `docs/design/core-luascript.md:3132` pointed at a phone
  log in the private playground repository. Fixed by the same commit; the
  older blobs stay in history and name only a file path, which needs no
  rewrite.
- **LOW, agent branch names in merge subjects** [C]: 106 merge commits name
  `worktree-agent-<hex>` branches. They reveal the workflow, not data. No
  action.
- **LOW, local-only refs** [C]: the 24 upstream tags, 93 local branches and
  the `refs/agents/c0add9c3-…` checkpoint refs (whose messages contain an
  agent session UUID) are not on GitHub. Never push with `--all`,
  `--mirror` or `--tags`.
- Informational [C]: commit timestamps carry the owner's UTC offset, and
  `Test/Core/Fixtures/luban-lua-lua/SOURCE.md` names the locale of the
  first luban run. Nothing to do.
- **Already public** [C]: the GitHub Pages site
  (`https://emindeniz99.github.io/xlua-ng/`, `"public": true`) is reachable
  today although the repository is private. Its sources are the guides
  listed in `docs/source/guides.mjs`; none of them contained any of the
  categories above.

## 3. History size

`git count-objects -vH`: 31.9 MB in packs, 7,557 blobs on `origin/main`
(249 MB uncompressed). Pushes and clones send the pack, so a clone is about
32 MB.

- Binaries whose paths no longer exist in the tree: 245 blobs, 73.8 MB raw,
  **18.1 MB in the pack (57% of it)** [C]. All of it is upstream's prebuilt
  plugins and tools: `Assets/Plugins/iOS/libxlua.a` (5.6 MB packed, 15
  revisions of about 2 MB each), `Assets/Plugins/x86_64`, `x86`, Android
  `x86` and `armeabi-v7a`, `WSA/*`, `Tools/*.exe|dll|pdb` (2.2 MB), the
  `xlua.bundle` macOS binary, and `Assets/XLua/Doc/XLua介绍.ppt` (0.8 MB).
  They were removed from the tree in `25fb824` and are ignored since.
- Binaries in the current tree: 8 small images and the three 1 KB signed
  example scripts [C].
- **Recommendation: do not slim.** Every one of those blobs was added by
  upstream commits. Removing them rewrites all 2,015 commits, so no commit
  of this repository would share a SHA with Tencent/xLua any more: GitHub
  comparisons, `git merge-base` against an upstream remote and SHA-based
  citations of upstream commits in `UPSTREAM.md` would stop working, for a
  clone that shrinks from about 32 MB to about 14 MB.

## 4. Licences and notices

- `LICENSE.TXT` keeps Tencent's original notice and MIT text, and lists
  every vendored component with its copyright: Lua 5.3.6, 5.4.9 and 5.5.1,
  LuaJIT 2.1 (`2a8e017e`), LuaSocket 3.1.0, Mono.Cecil 0.11.6 (fetched, not
  committed), the lua-snapshot port in `build/memory_leak_checker.c`,
  ios-cmake (BSD-3-Clause) and luaunit 3.5 (BSD-2-Clause) [C]. The vendored
  trees keep their own notices (`build/luajit-2.1/COPYRIGHT`,
  `build/luasocket/LICENSE`, the licence block in each `lua.h`) [C].
- Upstream's list (`b50e2803:LICENSE.TXT`) also named android-cmake and
  Cecil 0.9.6; both were removed from the tree, so dropping them from the
  notice is right [C].
- Files removed from the tree with third-party headers (Xxtea.cs by Ma
  Bingyao, the ltest framework) exist only in history, which carries their
  original headers [C].
- The luban fixture (`Test/Core/Fixtures/luban-lua-lua`) commits only
  output generated from the fixture's own schema and data; `SOURCE.md`
  records luban's licence and that none of its files are committed [C].
- `python3 Tools/dependencies.py --check`: "dependency inventory is up to
  date" [C]. `DEPENDENCIES.md` and the SBOM list the same vendored
  components as `LICENSE.TXT`.
- The UPM package ships `LICENSE.md` identical to `LICENSE.TXT` apart from
  line endings, and the C sources it includes under `build~` keep their
  headers [C].
- **LOW, owner question 4**: the xLua logo (`Assets/XLua/Doc/logo.png`,
  `docs/source/src/assets/logo.png`) is Tencent's artwork. The MIT licence
  covers copyright, not trademarks; an independent fork usually uses its
  own logo or states that the logo belongs to Tencent [U].

Nothing is missing a notice [C].

## 5. Workflows and repository settings

`zizmor --offline .github/workflows/`: 0 high, 0 medium, 3 low [C], all
`misfeature` for `shell: cmd` in `build.yml:292`, `native-build.yml:124`
and `publish.yml:290`. `.github/CI.md` says the run must report no
findings; zizmor 1.31.1 is newer than the 1.31.0 recorded in
`DEPENDENCIES.md`, so the rule is probably new [U]. **LOW**: switch the steps
to `pwsh` or add `# zizmor: ignore[misfeature]` with a reason.
`actionlint`: 23 findings, all `runner-label` for `ubuntu-26.04`, a label
this actionlint does not know yet; the hosted runs on that label passed [C].

Manual review [C]:

- No `pull_request_target`, no `workflow_run`, no `issue_comment` triggers.
- Every action is pinned to a full commit SHA with the version in a comment.
- Top-level `permissions` is `{}` or `contents: read` everywhere. Write
  scopes appear only on the jobs that need them: `security-events`
  (CodeQL), `pages` and `id-token` (docs deploy), `contents` (the release
  job of `publish.yml`), `checks` (Unity test reporter).
- Every `actions/checkout` sets `persist-credentials: false` (29 of 29).
- No event field reaches a `run:` script directly. `publish.yml` passes
  `inputs.tag_name` through `env` and validates it against
  `^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$` before use (lines 324 to 336).
  `unity-matrix.yml` passes the PR head repository through `env` and
  compares it with `github.repository` before it uses the Unity secrets
  (lines 40 to 46).
- `publish.yml` restores no cache. `native-build.yml`, `docs.yml` and the
  Unity `setup-*` steps set `cache: false`; `unity-matrix.yml` uses
  `actions/cache` for the Unity Library folder, which on `pull_request`
  writes only to the PR's own cache scope.
- Secrets used: `RENOVATE_TOKEN` (renovate.yml, dispatch only) and the
  Unity secrets (unity-matrix.yml). GitHub gives neither to workflows
  started by pull requests from forks.

What changes at the flip (ROADMAP "Before the repository is public"):

- Restore the commented triggers: `build.yml` (push on `build/**`),
  `codeql.yml` (push and pull request on `main`, weekly schedule),
  `native-build.yml` (pull request on build, runtime and test paths),
  `renovate.yml` (weekly schedule) and `unity-matrix.yml` (weekly
  schedule, push on `main`, pull request). `docs.yml` already runs on push.
  Standard hosted runners are free for public repositories, so the billing
  reason for manual-only goes away; larger runners are not free.
- `unity-matrix.yml` on `pull_request`: fork PRs get no secrets; the
  `license` job (lines 32 to 58) then reports a notice and the editor legs
  are skipped instead of failing [C]. Nothing to change.

Current repository settings (read with `gh api`) [C]: private, not a fork
on GitHub, Actions enabled with all actions allowed and SHA pinning not
required, default `GITHUB_TOKEN` read-only and unable to approve PRs, no
rulesets and no branch protection, wiki and issues on, merge, squash and
rebase merges all allowed, no description, no releases, 92 workflow runs
whose logs become public at the flip, Pages deployed and public.

| Setting | Works while private? | Note |
|---|---|---|
| Ruleset on `main` (no force push, no deletion) and on release tags | yes on a paid plan; Pages on a private repository shows the account has one [S] | ROADMAP item |
| Merge methods: merge commits only (AGENTS.md forbids squash) | yes | |
| Actions: require full-SHA pinning; allow only GitHub and selected actions | yes | every action is already pinned |
| Fork PR workflows: require approval for all outside collaborators | the option only matters once forks exist | keep the default or stricter, never relax it |
| Description, topics, website link, wiki off if unused | yes | |
| Dependabot alerts (Renovate reads them) | yes | |
| CodeQL upload, secret scanning, push protection, private vulnerability reporting | private needs GitHub Advanced Security; free once public | first step after the flip |

## 6. Code security quick pass

A checklist pass (memory safety at the C entry points, the core sandbox,
the signature loader, module loaders, Editor process calls, tool downloads)
over `build/xlua.c`, `i64lib.c`, `xlua_core*.c`, `memory_leak_checker.c`,
`Assets/XLua/Src` and `Tools/`, skipping everything already in
[bridge-audit.md](bridge-audit.md) and `Assets/XLua/Doc/Security.md`. It
read code only; nothing was built or reproduced, so [C] below means
"confirmed by reading the cited lines".

**Update (2026-10-18): both HIGH findings are fixed.** They were reproduced
on core builds of Lua 5.4.9 and 5.5.1 (macOS arm64) and fixed the same day;
evidence, fixes, cost and tests are in
[core-luascript.md, section 8.12](../design/core-luascript.md#812-c-loops-under-__len-and-handle-errors-under-xpcall-2026-10-18).
In short:

- **`__len` loops: FIXED, reproduced [C].** `table.remove`, `table.insert`
  and `table.sort` (with C metamethods) under a large `__len` ran until the
  test killed them with a 100,000-instruction limit set; `table.concat`
  stopped only at the memory limit. One more of the kind, not in the
  finding: `string.rep("", math.maxinteger)` on Lua 5.4.9. The loops now
  charge the C budget (`ltablib.c` of both versions), and 5.4.9's
  `string.rep` returns `""` for two empty strings as 5.5 does
  (`build/LUA_PATCHES.md`).
- **Stale `L->ci` in `xlua_core_handle`: FIXED, reproduced [C] for the
  handler and the stale frame, [U] for memory corruption.** The script's
  `xpcall` handler ran inside a raising handle operation; with a handler
  that raised, the operation returned `LUA_ERRERR` and the host function's
  frame had moved. ASan and UBSan with Lua's internal asserts and API
  checks reported no memory error, because the C function's return
  restores `L->ci` from a local copy. The operation now runs under
  `luaD_pcall` without a message handler.

The original findings, as read:

- **HIGH [C], core sandbox: `table.insert` and `table.remove` cannot be
  stopped under a script-chosen `__len`.** `tinsert` and `tremove`
  (`build/lua-5.5.1/src/ltablib.c:82-125`, the same in 5.4.9) take the
  length from `luaL_len`, which honours `__len`, and loop in C without the
  `luai_budget` ticks that `tmove` got (lines 157, 167).
  `core_setmetatable` (`build/xlua_core_sandbox.c:140-155`) refuses only
  `__gc`, so `table.remove(setmetatable({}, {__len = function() return
  math.maxinteger - 1 end}), 1)` runs up to 2^63 raw `geti`/`seti` steps.
  Nil stores allocate nothing, so `MemoryLimit` does not trip, and no hook
  fires, so `InstructionLimit`, `TimeLimit`, the watchdog and `Interrupt`
  cannot stop it. `table.sort` with a large `__len` and a C comparator such
  as `math.type` has the same shape. This contradicts the claim in
  Security.md (line 170) and `core-luascript.md` (lines 1202 to 1206 and 1325) that the C paths
  are proportional to input bounded by `MemoryLimit`. Fix: budget ticks in
  `tinsert`, `tremove` and `auxsort`, or ignore `__len` for these in core.
- **HIGH [C for the code path, S end to end], core sandbox:
  `xlua_core_handle` can run a script's message handler and return with a
  stale `L->ci`.** `xlua_core_handle` (`build/xlua_core_sandbox.c:1328`)
  calls `luaD_rawrunprotected` directly, which saves only `errorJmp` and
  `nCcalls`; unlike `luaD_pcall` it neither clears `L->errfunc` nor restores
  `L->ci`, `allowhook` or the stack. Handle operations that raise (SET with
  a nil or NaN key, NEXT with an invalid key) inside a host function that
  the script called through `xpcall(host, handler)` run the script's
  handler; if the handler itself errors, the longjmp lands with `L->ci`
  frames deep and the following `lua_settop` and API calls work on a dead
  frame, a plausible use-after-free. Fix: save and restore `ci`, `top`,
  `allowhook` and set `errfunc` to 0 around the call, or go through
  `lua_pcall` with a C body.
- **MED [C], `SignatureLoader` signs only the content**
  (`Assets/XLua/Src/SignatureLoader.cs:91-118`): the module name and a
  version are not covered, so whoever controls the transport can serve
  another signed module for `require "A"` or roll back to an older signed
  file. Verification itself fails closed and uses PKCS#1 v1.5 with SHA-256
  over the right bytes. Document it in `signature.md`, or sign name,
  version and content together (the planned signed loader in ROADMAP).
- **LOW [C]**: `Hotfix.cs:2089-2091` and `Generator.cs:2340` quote paths
  with `"` and join them into a `Process` argument string; a path that
  contains `"` splits arguments. No shell is involved and the paths are the
  developer's own.
- **LOW [C]**: `xlua.c:1040` `xlua_pushstruct` adds header sizes to a
  struct size that could wrap on 32-bit near `UINT_MAX`; the size comes
  from generated C#, not from Lua.
- **LOW [C]**: `Tools/luals.py:55` calls `extractall` without
  `filter="data"` (after a SHA-256 check).

Checked and clean [C]: the buffer API bounds and size checks
(`xlua_core_buffer.c`), `xlua.c` pack and unpack offset checks, `i64lib.c`
formatting buffers, `memory_leak_checker.c` buffers and its size cap, the
sandbox's removal of `string.dump`, forced text mode for `load` and
`require`, `collectgarbage` "count" only, coroutine budget handover, the
allocator's limit arithmetic, handle reference checks, module-name path
handling in the Resources and StreamingAssets loaders (`.` becomes `/`, so
`..` cannot be formed), and every tool download pinning a hash. Not
checked: LuaJIT and Lua 5.3 paths in depth, generated wrappers, CodeEmit.

Neither HIGH finding blocks the flip by itself: the core sandbox has not
been released (0.0.1-dev), and both are fixable in the open. They do break
promises Security.md makes today, so see owner question 6. (Both are fixed
now; see the update above.)

## History rewrite

Only one finding could justify a rewrite: the owner's email in the author
and committer fields (MED). Nothing else needs one: the work disk paths and
the private log citations in older blobs name only paths, and the binaries
are better kept (section 3).

If the owner wants the address replaced, it has to happen before the flip
(after it, forks and caches keep the old commits):

- `git filter-repo --mailmap` with one line mapping the Gmail address to
  the GitHub `noreply` address, on a fresh clone. The 972 upstream commits
  before `59bf4268` keep their SHAs; the 1,043 commits after it get new
  ones [C for the counts].
- filter-repo rewrites abbreviated SHAs in commit messages; 69 such
  references in 63 messages [C]. It does not rewrite file contents: 170
  citations of 81 post-fork SHAs in 23 files (most in `UPSTREAM.md`,
  `docs/design/source-generator.md`, `build/SIZE.md`,
  `docs/design/core-luascript.md`, `Test/README.md`) would need one
  follow-up commit using filter-repo's `commit-map` [C].
- Pushing the result is a force push of `main`, which AGENTS.md forbids, so
  it needs the owner's explicit decision. The 93 local branches, all
  worktrees and the Pages deployment refer to old SHAs; the branches would
  have to be rebased or dropped. Workflow run pages keep pointing at the
  old commits.
- Rescan with gitleaks and trufflehog after the rewrite.

Estimated effort: one to two hours, mostly the SHA citations in docs.

## Flip checklist

Before the flip:

1. Owner decides the email question; if rewriting, do it now and rescan.
2. Merge this audit's branch (docs path fixes, `.gitignore` safety net).
3. Settle owner questions 2 to 6 below.
4. Add a `SECURITY.md` naming the reporting channel (GitHub private
   vulnerability reporting once public), and point
   `Assets/XLua/Doc/Security.md` and `docs/design/core-luascript.md` (which
   notes the missing channel) at it.
5. Settings that work now: ruleset on `main` and release tags, merge
   commits only, Actions SHA pinning required, description and topics,
   wiki off if unused.
6. Decide whether to keep the 92 old run logs (they become public; no
   secret was ever configured, so they contain build output only [S]).
7. Rerun gitleaks over `--all` and the UPM tarball on the final `main`.

The flip:

8. Change visibility (owner only).

After the flip:

9. Turn on secret scanning, push protection and private vulnerability
   reporting; enable Dependabot alerts.
10. Restore the commented triggers (section 5) in one `ci(repo)` commit.
11. Run CodeQL with upload; add the Unity secrets if the editor matrix
    should run on CI.
12. Run `publish.yml` once as a draft prerelease and check that rebuilt
    plugins are identical (ROADMAP).
13. Check that the site's "View on GitHub" and edit links resolve.

## Owner questions

1. **Email in 1,043 commits:** keep the Gmail address public, or rewrite to
   the `noreply` address before the flip (force push of `main`)?
2. **Tool defaults naming the work disk** (section 2): keep, or replace by
   environment variables with neutral defaults?
3. **`.gitignore` safety net:** this audit adds secret file types and
   `.claude/settings.local.json` and `.claude/worktrees/`. Should all of
   `.claude/` be ignored, as the owner's local exclude does?
4. **Logo:** keep Tencent's xLua logo, add an attribution line, or use a new
   one?
5. **Demo private key in the UPM samples:** keep it (documented), or have
   the sample generate its own pair?
6. **Core sandbox HIGH findings (section 6):** fix both before the flip, or
   flip first and fix in the open? Either way, Security.md should not
   promise bounded C paths until the `__len` loops are budgeted.
   **Answered by events:** both were fixed on 2026-10-18 before the flip,
   and Security.md lists them as fixed.
