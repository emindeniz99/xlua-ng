# Documentation website

The site uses Hexo. English is the sole maintained documentation language.
See the [documentation index](../README.md) for historical upstream references.

## Sources

`Assets/XLua/Doc/`, the root README, contributor guide, and `docs/EXAMPLES.md`
are canonical. `sync.mjs` derives `src/` Markdown and rebases repository links
for the website. Edit the canonical documents, not those generated copies.
`themes/catlib/` contains the navigation, layout, and styling.

`../public/` is generated HTML. Do not edit it manually. No deployment or release
is performed by these commands.

## Local build

Install Node.js 20.19 or newer and npm, then run from this directory:

```sh
npm ci --ignore-scripts
npm run build
```

The build synchronizes Markdown, cleans old output, and regenerates HTML in
`docs/public/`. Commit canonical changes together with their generated output.
`npm run check` fails if generated Markdown differs from its canonical source.

For local preview:

```sh
npm run serve
```

Open `http://localhost:4000/xlua-ng/`. `_config.yml` defines the preview/deployment
base path; it does not enable GitHub Pages. The build uses pinned Hexo 8 packages and a committed dependency lock.
See [Hexo requirements](https://hexo.io/docs/#Required-Node-js-version).

The original theme attribution remains in its configuration. Tencent/xLua and
its third-party components retain their original licenses; see
[LICENSE.TXT](../../LICENSE.TXT).
