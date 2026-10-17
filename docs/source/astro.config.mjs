import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { defineConfig, passthroughImageService } from 'astro/config';
import starlight from '@astrojs/starlight';
import { base, groups } from './guides.mjs';

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
      social: [{ icon: 'github', label: 'GitHub', href: 'https://github.com/emindeniz99/xlua-ng' }],
      sidebar: groups.map(({ label, guides }) => ({
        label,
        items: guides.map(({ slug }) => slug),
      })),
      expressiveCode,
    }),
    requireCodeLanguage,
  ],
});
