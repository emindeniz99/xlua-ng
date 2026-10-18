# Security policy

xLua-NG is work in progress and has no release yet; fixes land on `main`.

## Reporting a vulnerability

Please report security problems privately through GitHub's
[private vulnerability reporting](https://github.com/emindeniz99/xlua-ng/security/advisories/new),
not in a public issue. Include the Lua version, the preset (`legacy`,
`minimal` or `core`), the platform and a script or steps that reproduce it.

## Scope

- `LuaScript` (the `core` preset, or `XLUA_LUASCRIPT=ON`) is meant to run
  untrusted scripts. What counts as a security bug there is listed in
  [Security.md](Assets/XLua/Doc/Security.md#luascript).
- `LuaEnv` is not a sandbox: a script in a `LuaEnv` can reach C# and is
  trusted code. See [Security.md](Assets/XLua/Doc/Security.md) for what that
  means and for hardening options.
