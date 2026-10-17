// Detects C# generic signatures that are written outside code. A browser reads
// `<T>` in `T GetInPath<T>(string path)` as an unknown HTML element and drops
// it, so the page shows `T GetInPath(string path)`. Both the Markdown check in
// sync.mjs and the built-HTML check in astro.config.mjs use this list of
// elements that are real markup.

const html = `a abbr address area article aside audio b base bdi bdo blockquote body br
button canvas caption cite code col colgroup data datalist dd del details dfn
dialog div dl dt em embed fieldset figcaption figure footer form h1 h2 h3 h4 h5
h6 head header hgroup hr html i iframe img input ins kbd label legend li link
main map mark menu meta meter nav noscript object ol optgroup option output p
param picture pre progress q rp rt ruby s samp script search section select slot
small source span strong style sub summary sup table tbody td template textarea
tfoot th thead time title tr track u ul var video wbr center font big tt strike
acronym`;

const svg = `svg g path circle ellipse line polyline polygon rect text tspan textpath
defs use symbol marker mask pattern clippath lineargradient radialgradient stop
filter image foreignobject switch view desc metadata animate animatetransform
animatemotion set mpath fegaussianblur feoffset feblend fecolormatrix
fecomposite femerge femergenode feflood feimage femorphology feturbulence
fedropshadow`;

const mathml = 'math mi mn mo ms mrow msup msub msubsup mfrac msqrt mroot mtext mspace mtable mtr mtd semantics annotation';

export const knownElements = new Set([...html.split(/\s+/), ...svg.split(/\s+/), ...mathml.split(/\s+/)]);

// An element name is markup when it is a known element or a custom element
// (the name has a hyphen, like Starlight's <starlight-tabs>). Names are
// compared in lower case because the HTML parser lower-cases them.
export function isMarkup(name) {
  const lower = name.toLowerCase();
  return knownElements.has(lower) || lower.includes('-');
}

// Start tags in the text, with quoted attribute values skipped so a `<` or `>`
// inside an attribute value does not count. The name must be followed by
// whitespace, `>`, `/`, `,` or `<`, or end the text; `<https://example.org>`
// and `<user@example.org>` are autolinks, not tags.
const startTag = /<([A-Za-z][A-Za-z0-9_.-]*)(?=[\s>/,<]|$)(?:"[^"]*"|'[^']*'|[^>"'<]|<(?=[A-Za-z]))*>?/g;

// Returns { name, index, context } for each start tag in `text` whose name is
// not markup. `index` is the offset in `text`; `context` is the text around
// the tag name on one line.
export function findGenerics(text) {
  const hits = [];
  const tag = new RegExp(startTag.source, 'g');
  let match;
  while ((match = tag.exec(text))) {
    if (isMarkup(match[1])) continue;
    // Resume right after the name so a following tag is still seen.
    tag.lastIndex = match.index + 1 + match[1].length;
    const from = Math.max(0, match.index - 30);
    const to = Math.min(text.length, match.index + 1 + match[1].length + 30);
    const context = text.slice(from, to).replace(/\s+/g, ' ').trim();
    hits.push({ name: match[1], index: match.index, context });
  }
  return hits;
}

// Replaces text that is allowed to contain `<` with spaces, keeping the line
// structure: fenced code blocks, inline code spans, HTML comments, and
// autolinks.
export function maskMarkdownCode(markdown) {
  const blank = text => text.replace(/[^\n]/g, ' ');
  let fence = null;
  const lines = markdown.split('\n').map(line => {
    const marker = /^\s*(`{3,}|~{3,})/.exec(line);
    if (fence) {
      if (marker && marker[1][0] === fence[0] && marker[1].length >= fence.length && /^\s*(`{3,}|~{3,})\s*$/.test(line)) fence = null;
      return blank(line);
    }
    if (marker) { fence = marker[1]; return blank(line); }
    return line;
  });
  return lines.join('\n')
    .replace(/(`+)(?!`)([\s\S]*?[^`])\1(?!`)/g, blank)
    .replace(/<!--[\s\S]*?-->/g, blank);
}
