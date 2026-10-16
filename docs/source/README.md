# Documentation website

The site uses [Astro Starlight](https://starlight.astro.build/). English is the
sole maintained documentation language. See the
[documentation index](../README.md) for historical upstream references.

## Sources

`Assets/XLua/Doc/`, the root README, contributor guide, `docs/EXAMPLES.md`, and
`Assets/XLua/CHANGELOG.txt` are canonical. Edit those, not the site.

`sync.mjs` is the only bridge. It turns each canonical file into a Starlight
page (frontmatter title, edit link, rebased repository links) under
`src/content/docs/` and writes the landing page. That directory is generated on
every run and git-ignored, so nothing generated is committed.
`guides.mjs` lists the guides: slug, title, source file, and sidebar group and
order. Add or reorder a page there.

`pnpm run check` runs the generation in memory and fails if a source file is
missing or a relative link points at nothing. No generated files are compared.

## Local build

Run `mise install` once. It installs the Node.js version pinned in the
repository's `mise.toml` (kept equal to `package.json#devEngines`) and the pnpm
version in `package.json#packageManager`.
Then run from this directory:

```sh
pnpm install --frozen-lockfile
pnpm run check
pnpm run build   # writes dist/ (git-ignored), including the search index
pnpm run links   # checks internal links and anchors in dist/
```

For a live preview run `pnpm run dev` and open `http://localhost:4321/xlua-ng/`.

`pnpm-workspace.yaml` holds the install policy: a seven-day release quarantine
and trust-downgrade checks. No dependency has an install script, so it has no
`allowBuilds` entries. Image optimization is turned off, so `sharp` is never
used.

## URLs

Each guide is published at `/xlua-ng/<slug>/` (Starlight's default layout), for
example `/xlua-ng/tutorial/`. The slugs are listed in `guides.mjs`. Old Hexo
URLs (`/xlua-ng/v1/guide/<slug>.html`) are not redirected.

## Deployment

`.github/workflows/docs.yml` builds the site and deploys it to GitHub Pages. It
runs manually only while the repository is private. Set the repository's Pages
source to "GitHub Actions" first. Running the build locally deploys nothing.

## Attribution

The previous Hexo theme, `catlib`, came from the Tencent/xLua documentation
site ("Copyright 2017 Tencent All Rights Reserved" in its theme configuration).
It was removed with Hexo. The xLua logo in `src/assets/logo.png` is the one from
that theme. Tencent/xLua and its third-party components retain their original
licenses; see [LICENSE.TXT](../../LICENSE.TXT).
