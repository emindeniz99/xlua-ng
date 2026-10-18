# Changelog

Changes of xLua-NG by version, newest first. The version comes from the
`VERSION` file; `Tools/make_upm_package.py` copies this file into the UPM
package as its `CHANGELOG.md`, and a release build stops when this file has no
section for the version. Every change in behaviour against Tencent/xLua, with
the way back where there is one, is in
[MIGRATING.md](https://github.com/emindeniz99/xlua-ng/blob/main/docs/MIGRATING.md).
Upstream's own history (2.1.15 and before) is in
[Assets/XLua/CHANGELOG.txt](https://github.com/emindeniz99/xlua-ng/blob/main/Assets/XLua/CHANGELOG.txt).

Before a release, rename the section below to the released version (for
example `## 0.1.0 (2026-11-02)`), set `VERSION` to the same version, and open
a new `## <next version>-dev (unreleased)` section above it
([docs/design/release.md](https://github.com/emindeniz99/xlua-ng/blob/main/docs/design/release.md)).

## 0.0.1-dev (unreleased)

The first version of the fork; nothing has been released yet. Compared with
Tencent/xLua 2.1.15:

- Lua 5.5.1 by default, with Lua 5.4.9, 5.3.6 and LuaJIT 2.1 as build
  options. 64-bit integers stay lossless.
- A Roslyn source generator writes the bindings while Unity compiles the
  scripts. **XLua > Generate Code**, its templates and `Assets/XLua/Gen` are
  gone; `GEN_CODE_MINIMIZE` selects the generator's smaller wrappers.
- Configuration with attributes, on types or at assembly level.
  **XLua > Migrate Configuration to Attributes** converts the static
  configuration lists.
- Presets `legacy` (the default), `minimal` and `core`, and a build option
  for every optional native part. The Unity build picks the native plugin
  variant of the preset (`Plugins/<platform>/xlua-variants/<name>/`).
- `LuaScript` (preview): a separate API with memory, instruction and time
  limits for scripts you do not trust.
- xLua compiles into the assemblies `XLua.Core`, `XLua`, `XLua.Core.Editor`
  and `XLua.Editor`. **XLua > Project Layout > Use Assembly-CSharp** goes
  back to upstream's layout in the Assets folder.
- A UPM package, `com.xlua-ng.xlua`, with the source generator, the native
  plugins of every platform for Lua 5.5.1, the WebGL sources, the guides and
  the examples as samples.
- Native plugins: Android API 23 with 16 KB pages, stripped, with the symbols
  as a separate download; iOS 15.0 with an iOS Simulator library; macOS 12.0;
  Windows ARM64.
- Bridge fixes for upstream issues
  ([UPSTREAM.md](https://github.com/emindeniz99/xlua-ng/blob/main/UPSTREAM.md))
  and opt-in hardening defines.
