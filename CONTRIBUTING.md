# Contributing to xLua-NG

xLua-NG is an independent community-maintained fork of Tencent/xLua. Keep the
`XLua` bridge API compatible unless a change explicitly documents a migration.
Preserve upstream authorship, copyright notices, and third-party licenses.

## Changes and validation

Use a focused branch and explain the problem, resulting behavior, and relevant
validation. Separate pristine runtime imports, version upgrades, and re-applied
integration patches so reviewers can inspect their provenance.

- Follow [native build instructions](build/README.md).
- Run the relevant [test suites](Test/README.md). A native compilation alone does
  not prove Unity player or device compatibility.
- Add regression coverage for behavior fixes. Preserve Unicode fixtures and
  historical test exclusions unless the change validates their replacement.
- Audit third-party licenses when upgrading a bundled runtime or dependency.
- State which platforms and runtime versions were tested and which remain untested.

## Documentation

English is the primary language. Consult the [documentation index and language
policy](docs/README.md). Compare the Chinese source with an existing English
version before replacing either; merge missing technical details and correct
mistranslations in context. Do not remove comments solely to eliminate Chinese.

Original Chinese documents are retained in [docs/zh-CN](docs/zh-CN/INDEX.md).
Chinese translations are welcome; state the English revision they cover and
whether they are outdated.

Edit canonical Markdown guides, then follow the [site build instructions](docs/source/README.md)
to regenerate site sources and HTML. Never hand-edit generated HTML.
