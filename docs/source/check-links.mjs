// Checks internal links and #anchors in the built site (run after `pnpm run build`).
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { base } from './guides.mjs';

const dist = path.join(path.dirname(fileURLToPath(import.meta.url)), 'dist');
const pages = (function walk(dir) {
  return readdirSync(dir).flatMap(name => {
    const file = path.join(dir, name);
    return statSync(file).isDirectory() ? walk(file) : file.endsWith('.html') ? [file] : [];
  });
})(dist);

const html = new Map(pages.map(file => [file, readFileSync(file, 'utf8')]));
const ids = file => new Set([...html.get(file).matchAll(/\sid="([^"]+)"/g)].map(m => m[1]));
let broken = 0, checked = 0;

for (const [file, text] of html) {
  const from = base + '/' + path.relative(dist, file).split(path.sep).join('/');
  for (const [, , raw] of text.matchAll(/\s(href|src)="([^"]*)"/g)) {
    if (!raw || /^(?:[a-z][a-z\d+.-]*:|\/\/)/i.test(raw)) continue;
    const [target, fragment] = raw.replace(/&amp;/g, '&').split('#');
    let url = new URL(target || from, 'http://x' + from).pathname;
    if (!url.startsWith(base + '/')) { console.error(`${from}: outside base: ${raw}`); broken++; continue; }
    url = url.slice(base.length);
    const local = path.join(dist, decodeURIComponent(url));
    const resolved = existsSync(local) && statSync(local).isDirectory() ? path.join(local, 'index.html') : local;
    checked++;
    if (!existsSync(resolved)) { console.error(`${from}: missing target: ${raw}`); broken++; continue; }
    if (fragment && fragment !== '_top' && html.has(resolved) && !ids(resolved).has(decodeURIComponent(fragment))) {
      console.error(`${from}: missing anchor: ${raw}`); broken++;
    }
  }
}
console.log(`${pages.length} pages, ${checked} internal links checked, ${broken} broken.`);
process.exit(broken ? 1 : 0);
