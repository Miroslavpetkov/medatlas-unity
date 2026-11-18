import * as esbuild from 'esbuild';
const stub = {
  name: 'stubs',
  setup(b) {
    b.onResolve({ filter: /(^|\/)i18n$/ }, () => ({ path: 'i18n', namespace: 'stub' }));
    b.onLoad({ filter: /^i18n$/, namespace: 'stub' }, () => ({ contents: 'export const getLang=()=>"bg"; export const tx=(en,bg)=>({en,bg}); export const T=(t)=>t&&t.bg?t.bg:t; export const UI={};', loader: 'js' }));
  },
};
await esbuild.build({ entryPoints: ['entry.ts'], bundle: true, format: 'iife', target: 'es2017', minify: true, outfile: 'medphysio.js', plugins: [stub], logLevel: 'warning', nodePaths: ['node_modules'] });
