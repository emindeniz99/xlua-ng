import { defineConfig, passthroughImageService } from 'astro/config';
import starlight from '@astrojs/starlight';
import { base, groups, routeOf } from './guides.mjs';

export default defineConfig({
  site: 'https://emindeniz99.github.io',
  base,
  trailingSlash: 'never',
  // Pages stay at /xlua-ng/v1/guide/<slug>.html, as on the old Hexo site.
  build: { format: 'file' },
  // The site has no raster content that needs resizing; skip sharp.
  image: { service: passthroughImageService() },
  redirects: {
    // Old Hexo URLs: the Introduction page is now v1/guide.html (see guides.mjs),
    // and the README was also published at readme.html.
    '/v1/guide/index': `${base}/v1/guide.html`,
    '/readme': `${base}/v1/guide.html`,
  },
  integrations: [
    starlight({
      title: 'xLua-NG',
      description: 'Lua scripting for Unity, .NET, and Mono. An independent community-maintained fork of Tencent/xLua.',
      logo: { src: './src/assets/logo.png', alt: 'xLua' },
      social: [{ icon: 'github', label: 'GitHub', href: 'https://github.com/emindeniz99/xlua-ng' }],
      sidebar: groups.map(({ label, guides }) => ({
        label,
        items: guides.map(({ slug }) => ({ slug: routeOf(slug) })),
      })),
    }),
  ],
});
