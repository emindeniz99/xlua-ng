// The guide list, shared by sync.mjs (content) and astro.config.mjs (sidebar).
// Each guide is built from a canonical Markdown file and published at
// /xlua-ng/<slug>/. Group and array order give the sidebar order.
export const groups = [
  {
    label: 'Getting started',
    guides: [
      { slug: 'introduction', title: 'Introduction', source: 'README.md' },
      { slug: 'examples', title: 'Examples', source: 'docs/EXAMPLES.md' },
      { slug: 'contributing', title: 'Contributing', source: 'CONTRIBUTING.md' },
      { slug: 'migrating', title: 'Migrating from xLua', source: 'docs/MIGRATING.md' },
      { slug: 'features', title: 'Features', source: 'Assets/XLua/Doc/features.md' },
    ],
  },
  {
    label: 'Guides',
    guides: [
      { slug: 'tutorial', title: 'Tutorial', source: 'Assets/XLua/Doc/XLua_Tutorial_EN.md' },
      { slug: 'configure', title: 'Configuration', source: 'Assets/XLua/Doc/Configure_EN.md' },
      { slug: 'migrating-configuration', title: 'Converting configuration lists', source: 'Assets/XLua/Doc/Migrating_Configuration.md' },
      { slug: 'api', title: 'API reference', source: 'Assets/XLua/Doc/XLua_API_EN.md' },
      { slug: 'loaders', title: 'Loading Lua sources', source: 'Assets/XLua/Doc/Lua_Loaders.md' },
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
      { slug: 'native-extensions', title: 'Native Lua extensions', source: 'Assets/XLua/Doc/Add_Remove_Lua_Lib.md' },
      { slug: 'custom-generation', title: 'Custom code generation', source: 'Assets/XLua/Doc/Custom_Generate_EN.md' },
      { slug: 'luacats', title: 'LuaCATS definitions', source: 'Assets/XLua/Doc/LuaCATS.md' },
      { slug: 'ecosystem', title: 'Ecosystem', source: 'docs/ECOSYSTEM.md' },
      { slug: 'compatible-bytecode', title: 'Compatible bytecode', source: 'Assets/XLua/Doc/compatible_bytecode.md' },
      { slug: 'security', title: 'Security and sandboxing', source: 'Assets/XLua/Doc/Security.md' },
      { slug: 'changelog', title: 'Upstream release history', source: 'Assets/XLua/CHANGELOG.txt' },
    ],
  },
];

export const guides = groups.flatMap(group => group.guides);

export const base = '/xlua-ng';

// Site-rooted URL of a guide page.
export const urlOf = slug => `${base}/${slug}/`;
