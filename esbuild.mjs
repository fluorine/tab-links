import { build, context } from 'esbuild';
const options = {
  entryPoints: ['src/extension.ts'], bundle: true, outfile: 'dist/extension.js',
  platform: 'node', format: 'cjs', target: 'node20', external: ['vscode'], sourcemap: false, minify: true,
};
if (process.argv.includes('--watch')) { (await context(options)).watch(); } else { await build(options); }
