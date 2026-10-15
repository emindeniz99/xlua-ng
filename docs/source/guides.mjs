// The guide list, shared by sync.mjs (content) and astro.config.mjs (sidebar).
// Each guide is built from a canonical Markdown file and published at
// /xlua-ng/v1/guide/<slug>.html. Group and array order give the sidebar order.
export const groups = [
  {
    label: 'Getting started',
    guides: [
      { slug: 'index', title: 'Introduction', source: 'README.md' },
      { slug: 'use', title: 'Examples', source: 'docs/EXAMPLES.md' },
      { slug: 'contribution', title: 'Contributing', source: 'CONTRIBUTING.md' },
      { slug: 'features', title: 'Features', source: 'Assets/XLua/Doc/features.md' },
    ],
  },
  {
    label: 'Guides',
    guides: [
      { slug: 'tutorial', title: 'Tutorial', source: 'Assets/XLua/Doc/XLua_Tutorial_EN.md' },
      { slug: 'configure', title: 'Configuration', source: 'Assets/XLua/Doc/Configure_EN.md' },
      { slug: 'api', title: 'API reference', source: 'Assets/XLua/Doc/XLua_API_EN.md' },
      { slug: 'faq', title: 'FAQ', source: 'Assets/XLua/Doc/Faq_EN.md' },
    ],
  },
  {
    label: 'More guides',
    guides: [
      { slug: 'hotfix', title: 'Hotfixing', source: 'Assets/XLua/Doc/Hotfix_EN.md' },
      { slug: 'gc-optimization', title: 'GC optimization', source: 'Assets/XLua/Doc/GC_Optimization.md' },
      { slug: 'performance-analysis', title: 'Performance analysis', source: 'Assets/XLua/Doc/Performance_Analysis.md' },
      { slug: 'signature', title: 'Script signatures', source: 'Assets/XLua/Doc/signature.md' },
      { slug: 'crtdel-3rd', title: 'Native Lua extensions', source: 'Assets/XLua/Doc/Add_Remove_Lua_Lib.md' },
      { slug: 'custom-generation', title: 'Custom code generation', source: 'Assets/XLua/Doc/Custom_Generate_EN.md' },
      { slug: 'compatible-bytecode', title: 'Compatible bytecode', source: 'Assets/XLua/Doc/compatible_bytecode.md' },
      { slug: 'security', title: 'Security and sandboxing', source: 'Assets/XLua/Doc/Security.md' },
      { slug: 'version', title: 'Upstream release history', source: 'Assets/XLua/CHANGELOG.txt' },
    ],
  },
];

export const guides = groups.flatMap(group => group.guides);

export const base = '/xlua-ng';

// Astro drops a trailing "/index" from content slugs, so the Introduction page
// has the slug "v1/guide" and is built as v1/guide.html. astro.config.mjs
// redirects the old v1/guide/index.html to it.
export const routeOf = slug => (slug === 'index' ? 'v1/guide' : `v1/guide/${slug}`);

// Site-rooted URL of a guide page (the site builds with build.format: 'file').
export const urlOf = slug => `${base}/${routeOf(slug)}.html`;
