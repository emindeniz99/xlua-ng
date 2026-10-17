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
missing, a relative link points at nothing, or a C# generic is written outside
code (see below). No generated files are compared.

## Code blocks

Starlight highlights code blocks at build time with Expressive Code and Shiki;
the pages carry no highlighter JavaScript. The `expressiveCode` settings in
`astro.config.mjs` select the `github-dark` and `github-light` themes, which
follow the site's theme switch, and map fence tags Shiki does not know (for
example `h` to `c`). Shiki already accepts `c#`, `cs`, `sh`, `shell`, `bat`,
`cmd` and `yml`. A tag that neither knows is shown as plain text with a build
warning.

Every code block in the canonical Markdown needs a fenced tag: `csharp`, `lua`,
`c`, `sh`, `cmake`, `json`, `xml`, and `text` for plain output or prose.
`pnpm run build` fails on an untagged fence or an indented code block (the
`requireCodeLanguage` hook in `astro.config.mjs`).

## C# generics outside code

A browser reads `<T>` in `T GetInPath<T>(string path)` as an unknown HTML
element and drops it, so a heading or a sentence outside backticks shows
`T GetInPath(string path)`. Put every generic signature or type name
(`List<Type>`, `Dictionary<TKey, TValue>`) in a code span or a fence. Two
checks catch the mistake, both using the element list in `generics.mjs`:

- `sync.mjs` (so `pnpm run check`, and the start of `pnpm run build`) scans the
  canonical Markdown outside fences, inline code and HTML comments for a start
  tag whose name is not an HTML, SVG or MathML element and has no hyphen
  (custom elements such as `<starlight-tabs>` are fine). It reports the file,
  the line and the text around the tag.
- The `rejectUnknownElements` hook in `astro.config.mjs` runs the same test on
  the built HTML, outside `<pre>`, `<code>`, `<script>`, `<style>` and
  comments, and fails the build with the page and the surrounding text.
  Quoted attribute values are never read as tags: Expressive Code keeps a code
  block's text in a `data-code` attribute, where `<` is legal.

Real HTML in the Markdown must use a known element name. A new one that the
list lacks is added to `generics.mjs`.

Astro caches rendered pages in `node_modules/.astro` and does not notice a
change to the Expressive Code settings, so `pnpm run build` passes `--force`.
Without it, pages keep the old colours and link a stylesheet that no longer
exists. `pnpm run dev` still uses the cache.

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
