import path from 'node:path';
import { pathToFileURL, fileURLToPath } from 'node:url';
const root = path.dirname(fileURLToPath(import.meta.url));
const modules = process.env.EGOIST_WEB_MODULES || path.join(root, 'node_modules');
const { build } = await import(pathToFileURL(path.join(modules, 'vite/dist/node/index.js')).href);
await build({ root, configFile: false, base: './', cacheDir: process.env.EGOIST_UI_CACHE,
  resolve: { alias: { 'react-dom': path.join(modules, 'react-dom'), react: path.join(modules, 'react') } },
  build: { outDir: process.env.EGOIST_UI_OUTPUT || path.join(root, 'dist'), emptyOutDir: false, minify: true },
});
