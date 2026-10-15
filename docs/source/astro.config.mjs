import { defineConfig, passthroughImageService } from 'astro/config';
import starlight from '@astrojs/starlight';
import { base, groups } from './guides.mjs';

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
    }),
  ],
});
