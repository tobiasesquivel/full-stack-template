import fs from 'node:fs'; import path from 'node:path';
const [src, out] = process.argv.slice(2);
const dir = path.dirname(src);
let html = fs.readFileSync(src, 'utf8');
html = html.replace(/<link rel="stylesheet" href="([^"]+)">/g, (_, h) => `<style>${fs.readFileSync(path.join(dir, h), 'utf8')}</style>`);
html = html.replace(/<script src="([^"]+)"><\/script>/g, (_, h) => `<script>${fs.readFileSync(path.join(dir, h), 'utf8')}</script>`);
fs.writeFileSync(out, html);
