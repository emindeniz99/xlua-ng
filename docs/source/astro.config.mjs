import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig, passthroughImageService } from 'astro/config';
import starlight from '@astrojs/starlight';
import { base, groups } from './guides.mjs';
import { findGenerics } from './generics.mjs';

// Code blocks are highlighted at build time by Expressive Code (bundled with
// Starlight) with Shiki; the pages carry no highlighter JavaScript. The GitHub
// themes follow Starlight's light/dark switch. Astro's cache of rendered pages
// does not notice a change here, so `pnpm run build` passes --force.
const expressiveCode = {
  themes: ['github-dark', 'github-light'],
  // Code block backgrounds and borders in the site's own greys.
  useStarlightUiThemeColors: true,
  shiki: {
    // Fence tags Shiki does not know, mapped to a grammar it has. Shiki
    // already accepts c#, cs, sh, shell, zsh, bat, cmd, yml, js, ts, py, c++
    // and objc. Any other unknown tag is shown as plain text with a warning.
    langAlias: {
      'C#': 'csharp',
      CSharp: 'csharp',
      Lua: 'lua',
      h: 'c',
      hpp: 'cpp',
      mm: 'objective-cpp',
      gradle: 'groovy',
      csproj: 'xml',
      props: 'xml',
      targets: 'xml',
      asmdef: 'json',
    },
  },
};

// Fails the build when a page has a code block without a language: an
// untagged fence or an indented code block. Expressive Code renders those as
// <pre data-language="plaintext"> without highlighting. Plain output takes the
// `text` tag. This runs on the built HTML because an error thrown while
// Starlight renders a page is logged and the page is published empty.
const requireCodeLanguage = {
  name: 'xlua-ng:require-code-language',
  hooks: {
    'astro:build:done': ({ dir }) => {
      const dist = fileURLToPath(dir);
      const problems = [];
      for (const file of readdirSync(dist, { recursive: true })) {
        if (!file.endsWith('.html')) continue;
        const html = readFileSync(path.join(dist, file), 'utf8');
        for (const match of html.matchAll(/<pre data-language="plaintext">(.*?)<\/pre>/gs)) {
          const first = match[1].replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 60);
          problems.push(`${file}: code block without a language, starting "${first}"`);
        }
      }
      if (problems.length) {
        throw new Error(
          `${problems.join('\n')}\nTag each fence in the canonical Markdown (\`\`\`csharp, \`\`\`lua, ` +
            '```text for plain output); indented code blocks cannot carry a tag.'
        );
      }
    },
  },
};

// Fails the build when a page has a C# generic written outside code, such as
// `T GetInPath<T>(string path)` or `Dictionary<TKey, TValue>` in a heading or
// in prose. The browser reads `<T>` as an unknown element and drops it, so the
// page shows `GetInPath(string path)`. Markdown that is not in a code span or
// fence reaches the HTML as a start tag whose name is not an HTML, SVG or
// MathML element (custom elements with a hyphen, such as Starlight's own, are
// fine). Code blocks, inline code, scripts and styles are skipped, and quoted
// attribute values are never read as tags: Expressive Code keeps a block's text
// in a `data-code` attribute, where `<` is legal.
const rejectUnknownElements = {
  name: 'xlua-ng:reject-unknown-elements',
  hooks: {
    'astro:build:done': ({ dir }) => {
      const dist = fileURLToPath(dir);
      const problems = [];
      for (const file of readdirSync(dist, { recursive: true })) {
        if (!file.endsWith('.html')) continue;
        const html = readFileSync(path.join(dist, file), 'utf8')
          .replace(/<!--[\s\S]*?-->/g, '')
          .replace(/<(pre|code|script|style)\b[^>]*>[\s\S]*?<\/\1>/gi, '');
        for (const { name, context } of findGenerics(html)) {
          problems.push(`${file}: unknown HTML element <${name}> outside code, near "${context}"`);
        }
      }
      if (problems.length) {
        throw new Error(
          `${problems.join('\n')}\nA C# generic such as List<Type> or GetInPath<T>() outside a code span or fence ` +
            'is read as an HTML tag and disappears from the page. Put it in backticks in the canonical Markdown, ' +
            'for example `T GetInPath<T>(string path)`.'
        );
      }
    },
  },
};

export default defineConfig({
  site: 'https://emindeniz99.github.io',
  base,
  // The site has no raster content that needs resizing; skip sharp.
  image: { service: passthroughImageService() },
  integrations: [
    starlight({
      title: 'xLua-NG',
      description: 'Lua scripting for Unity, .NET, and Mono. An independent community-maintained fork of Tencent/xLua.',
      logo: { src: './src/assets/logo.png', alt: 'xLua' },
      // The badge row on the home and introduction pages (sync.mjs).
      customCss: ['./src/styles/custom.css'],
      social: [{ icon: 'github', label: 'GitHub', href: 'https://github.com/emindeniz99/xlua-ng' }],
      sidebar: groups.map(({ label, guides }) => ({
        label,
        items: guides.map(({ slug }) => slug),
      })),
      expressiveCode,
    }),
    requireCodeLanguage,
    rejectUnknownElements,
  ],
});
