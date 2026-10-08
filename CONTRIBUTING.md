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

Contributions from developers of all nationalities are welcome. English is
preferred for issues and pull requests and is the sole maintained documentation
language. Consult the [documentation index and language policy](docs/README.md).

Translate useful Chinese-only content and merge missing technical details before
removing redundant documents. We do not maintain synchronized multilingual
copies. Translate comments in context; do not remove functional strings, Unicode
fixtures, identifiers, or required notices to eliminate Chinese characters.
Original sources remain accessible through the historical references in the index.

Edit canonical Markdown guides, then follow the [site build instructions](docs/source/README.md)
to regenerate site sources and HTML. Never hand-edit generated HTML.
