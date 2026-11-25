import * as esbuild from 'esbuild';
import path from 'node:path';
// The models import i18n for their bg/en texts; Unity shows the Bulgarian ones.
const stub = {
  name: 'stubs',
  setup(b) {
    b.onResolve({ filter: /(^|\/)i18n$/ }, () => ({ path: 'i18n', namespace: 'stub' }));
    b.onLoad({ filter: /^i18n$/, namespace: 'stub' }, () => ({ contents: 'export const getLang=()=>"bg"; export const setLang=()=>{}; export const tx=(en,bg)=>({en,bg}); export const T=(t)=>t&&t.bg?t.bg:t; export const t=(x)=>x&&x.bg!==undefined?x.bg:x; export const ut=(u)=>!u?"":typeof u==="string"?u:u.bg; export const other=(x)=>x.en; export const UI=new Proxy({}, {get:()=>({en:"",bg:""})}); export const CATEGORY_META=new Proxy({}, {get:()=>({label:{en:"",bg:""},icon:"",color:"#fff"})});', loader: 'js' }));
  },
};
await esbuild.build({
  entryPoints: [process.argv[2] ?? 'entry.ts'], bundle: true, format: 'iife', target: 'es2019', minify: !process.argv[3], outfile: process.argv[3] ?? 'medphysio.js',
  plugins: [stub], logLevel: 'warning', nodePaths: ['node_modules'],
  // one copy of three.js for the scenes and the mirror
  alias: { three: path.resolve('node_modules/three') },
  define: { 'import.meta.env.VITE_BODY_B64': 'false', 'import.meta.env.BASE_URL': '"/"', 'import.meta.env.DEV': 'false' },
});
