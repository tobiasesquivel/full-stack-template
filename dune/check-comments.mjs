import { readdir, readFile, stat, writeFile, rename, mkdtemp, rm } from 'node:fs/promises';
import path from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createRequire } from 'node:module';

const here = path.dirname(fileURLToPath(import.meta.url));
const require = createRequire(path.join(here, '..', 'frontend', 'package.json'));
const ts = require('typescript');
const postcss = require('postcss');
const YAML = require('yaml');
const angularCompilerPath = require.resolve('@angular/compiler');
const angularCompiler = await import(pathToFileURL(angularCompilerPath).href);

const TS_EXTENSIONS = new Set(['.ts', '.mts', '.cts', '.js', '.mjs', '.cjs', '.jsx', '.tsx']);
const HTML_EXTENSIONS = new Set(['.html']);
const CSS_EXTENSIONS = new Set(['.css']);
const XML_EXTENSIONS = new Set(['.xml']);
const YAML_EXTENSIONS = new Set(['.yaml', '.yml']);
const CONFIG_EXTENSIONS = new Set(['.editorconfig']);
const DOCKER_NAMES = new Set(['Dockerfile', 'dockerfile']);
const SYMBOLS_NAMES = new Set(['BannedSymbols.txt']);

function toPosix(value) {
  return value.split(path.sep).join('/');
}

function offsetToLine(text, offset) {
  let line = 1;
  for (let index = 0; index < offset && index < text.length; index += 1) {
    if (text[index] === '\n') line += 1;
  }
  return line;
}

function isGeneratedPath(rel) {
  if (rel.includes('node_modules/')) return true;
  if (rel.startsWith('frontend/.angular/')) return true;
  if (rel.includes('/obj/') || rel.includes('/bin/')) return true;
  if (rel.endsWith('.d.ts')) return true;
  if (rel.startsWith('openspec/changes/archive/')) return true;
  return false;
}

function isMarkdownPath(rel) {
  return rel.endsWith('.md');
}

function isJsonPath(rel) {
  return rel.endsWith('.json');
}

export function resolveRoot(input) {
  if (input) return path.resolve(input);
  return path.resolve(here, '..');
}

async function collectFiles(root) {
  const out = [];
  async function walk(dir) {
    const entries = await readdir(dir, { withFileTypes: true });
    for (const entry of entries) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        if (entry.name === '.git' || entry.name === 'node_modules' || entry.name === '.angular') continue;
        await walk(full);
      } else if (entry.isFile()) {
        out.push(full);
      }
    }
  }
  await walk(root);
  out.sort();
  return out;
}

function scriptKindFor(fileName) {
  if (fileName.endsWith('.tsx') || fileName.endsWith('.jsx')) return ts.ScriptKind.TSX;
  if (fileName.endsWith('.ts') || fileName.endsWith('.mts') || fileName.endsWith('.cts')) return ts.ScriptKind.TS;
  return ts.ScriptKind.JS;
}

export function inspectTs(text, fileName) {
  const sourceFile = ts.createSourceFile(fileName, text, ts.ScriptTarget.ES2022, true, scriptKindFor(fileName));
  const diagnostics = [];
  const ranges = [];
  const seen = new Set();
  function record(pos, end) {
    const key = `${pos}:${end}`;
    if (seen.has(key)) return;
    seen.add(key);
    ranges.push({ pos, end });
    const { line } = sourceFile.getLineAndCharacterOfPosition(pos);
    diagnostics.push({ line: line + 1, pos, end });
  }
  function walk(node) {
    const full = sourceFile.getFullText();
    for (const comment of ts.getLeadingCommentRanges(full, node.getFullStart()) ?? []) record(comment.pos, comment.end);
    for (const comment of ts.getTrailingCommentRanges(full, node.getEnd()) ?? []) record(comment.pos, comment.end);
    ts.forEachChild(node, walk);
  }
  walk(sourceFile);
  ranges.sort((a, b) => a.pos - b.pos);
  diagnostics.sort((a, b) => a.line - b.line || a.pos - b.pos);
  return { diagnostics, ranges };
}

export function inspectHtml(text, fileName) {
  const parser = new angularCompiler.HtmlParser();
  const result = parser.parse(text, fileName ?? 'template.html');
  if (result.errors.length > 0) throw new Error(`cannot parse ${fileName ?? 'template.html'}: ${result.errors[0].msg}`);
  const spans = [];
  function walk(nodes) {
    for (const node of nodes ?? []) {
      if (node instanceof angularCompiler.Comment) {
        spans.push({ pos: node.sourceSpan.start.offset, end: node.sourceSpan.end.offset });
      }
      if (node.children) walk(node.children);
    }
  }
  walk(result.rootNodes);
  return toDiagnostics(text, spans);
}

export function inspectXml(text, fileName) {
  const parser = new angularCompiler.XmlParser();
  const result = parser.parse(text, fileName ?? 'config.xml');
  if (result.errors.length > 0) throw new Error(`cannot parse ${fileName ?? 'config.xml'}: ${result.errors[0].msg}`);
  const spans = [];
  function walk(nodes) {
    for (const node of nodes ?? []) {
      if (node instanceof angularCompiler.Comment) {
        spans.push({ pos: node.sourceSpan.start.offset, end: node.sourceSpan.end.offset });
      }
      if (node.children) walk(node.children);
    }
  }
  walk(result.rootNodes);
  return toDiagnostics(text, spans);
}

export function inspectCss(text, fileName) {
  let root;
  try {
    root = postcss.parse(text, { from: fileName });
  } catch (error) {
    throw new Error(`cannot parse ${fileName}: ${error.message}`);
  }
  const spans = [];
  root.walkComments((comment) => {
    const start = comment.source.start;
    const end = comment.source.end;
    if (!start || !end || start.offset === undefined || end.offset === undefined) {
      throw new Error(`cannot locate comment in ${fileName}`);
    }
    spans.push({ pos: start.offset, end: end.offset });
  });
  return toDiagnostics(text, spans);
}

export function inspectYaml(text, fileName) {
  let documents;
  try {
    documents = YAML.parseAllDocuments(text);
  } catch (error) {
    throw new Error(`cannot parse ${fileName}: ${error.message}`);
  }
  for (const document of documents) {
    if (document.errors.length > 0) throw new Error(`cannot parse ${fileName}: ${document.errors[0].message}`);
  }
  const spans = [];
  const lines = text.split('\n');
  const lineStarts = [0];
  for (let index = 0; index < text.length; index += 1) {
    if (text[index] === '\n') lineStarts.push(index + 1);
  }
  for (const document of documents) {
    YAML.visit(document, (key, node) => {
      if (!YAML.isNode(node)) return;
      if (node.comment) {
        const range = node.range;
        if (range) spans.push(resolveInlineComment(text, lines, lineStarts, range[1]));
      }
      if (node.commentBefore) {
        const range = node.range;
        if (range) {
          for (const span of resolveBeforeComment(text, lines, lineStarts, range[0], node.commentBefore)) {
            spans.push(span);
          }
        }
      }
    });
    if (document.comment) {
      const span = findTrailingComment(text, lines, lineStarts, text.length);
      if (span) spans.push(span);
    }
    if (document.commentBefore) {
      for (const span of resolveBeforeComment(text, lines, lineStarts, 0, document.commentBefore)) {
        spans.push(span);
      }
    }
  }
  return toDiagnostics(text, spans.filter(Boolean));
}

function resolveInlineComment(text, lines, lineStarts, valueEnd) {
  let lineIndex = 0;
  for (let index = 0; index < lineStarts.length; index += 1) {
    if (lineStarts[index] <= valueEnd) lineIndex = index;
    else break;
  }
  const line = lines[lineIndex] ?? '';
  const lineStart = lineStarts[lineIndex] ?? 0;
  const searchFrom = Math.max(0, valueEnd - lineStart);
  const hash = line.indexOf('#', searchFrom);
  if (hash < 0) throw new Error('cannot locate YAML comment');
  return { pos: lineStart + hash, end: lineStart + line.length };
}

function resolveBeforeComment(text, lines, lineStarts, nodeStart, commentBefore) {
  void text;
  const wanted = commentBefore.split('\n').length;
  let nodeLine = 0;
  for (let index = 0; index < lineStarts.length; index += 1) {
    if (lineStarts[index] <= nodeStart) nodeLine = index;
    else break;
  }
  const spans = [];
  let remaining = wanted;
  for (let lineIndex = nodeLine - 1; lineIndex >= 0 && remaining > 0; lineIndex -= 1) {
    const line = lines[lineIndex] ?? '';
    const trimmed = line.trim();
    if (trimmed === '') continue;
    if (trimmed.startsWith('#')) {
      const hash = line.indexOf('#');
      spans.push({ pos: lineStarts[lineIndex] + hash, end: lineStarts[lineIndex] + line.length });
      remaining -= 1;
    } else {
      break;
    }
  }
  if (spans.length !== wanted) throw new Error('cannot locate YAML comment');
  return spans.reverse();
}

function findTrailingComment(text, lines, lineStarts, fromOffset) {
  void fromOffset;
  for (let lineIndex = lines.length - 1; lineIndex >= 0; lineIndex -= 1) {
    const trimmed = (lines[lineIndex] ?? '').trim();
    if (trimmed === '') continue;
    if (trimmed.startsWith('#')) {
      const line = lines[lineIndex];
      const hash = line.indexOf('#');
      return { pos: lineStarts[lineIndex] + hash, end: lineStarts[lineIndex] + line.length };
    }
    return null;
  }
  return null;
}

export function inspectEditorconfig(text) {
  const spans = [];
  const lines = text.split('\n');
  let offset = 0;
  for (const line of lines) {
    const hash = line.indexOf('#');
    if (hash >= 0) spans.push({ pos: offset + hash, end: offset + line.length });
    offset += line.length + 1;
  }
  return toDiagnostics(text, spans);
}

export function inspectDockerfile(text) {
  const spans = [];
  const lines = text.split('\n');
  let offset = 0;
  for (const line of lines) {
    const trimmed = line.trimStart();
    if (trimmed.startsWith('#')) {
      const hash = line.indexOf('#');
      spans.push({ pos: offset + hash, end: offset + line.length });
    }
    offset += line.length + 1;
  }
  return toDiagnostics(text, spans);
}

export function inspectSymbols(text) {
  return inspectDockerfile(text);
}

function toDiagnostics(text, spans) {
  const unique = [...new Map(spans.map((span) => [`${span.pos}:${span.end}`, span])).values()];
  unique.sort((a, b) => a.pos - b.pos);
  return {
    diagnostics: unique.map((span) => ({ line: offsetToLine(text, span.pos), pos: span.pos, end: span.end })),
    ranges: unique,
  };
}

export function inspectSource(rel, text) {
  const extension = path.extname(rel).toLowerCase();
  const base = path.basename(rel);
  if (TS_EXTENSIONS.has(extension)) return inspectTs(text, base);
  if (HTML_EXTENSIONS.has(extension)) return inspectHtml(text, rel);
  if (CSS_EXTENSIONS.has(extension)) return inspectCss(text, rel);
  if (XML_EXTENSIONS.has(extension)) return inspectXml(text, rel);
  if (YAML_EXTENSIONS.has(extension)) return inspectYaml(text, rel);
  if (CONFIG_EXTENSIONS.has(extension)) return inspectEditorconfig(text);
  if (DOCKER_NAMES.has(base)) return inspectDockerfile(text);
  if (SYMBOLS_NAMES.has(base)) return inspectSymbols(text);
  return null;
}

export function applyFix(text, ranges) {
  const newlineAt = new Set();
  for (let index = 0; index < text.length; index += 1) {
    if (text[index] === '\n') newlineAt.add(index);
  }
  let out = '';
  let cursor = 0;
  const ordered = [...ranges].sort((a, b) => a.pos - b.pos);
  for (const span of ordered) {
    if (span.pos < cursor) continue;
    out += text.slice(cursor, span.pos);
    const removed = text.slice(span.pos, span.end);
    const newlines = (removed.match(/\n/g) ?? []).length;
    const touchesStart = span.pos === 0 || newlineAt.has(span.pos - 1) || /\s/.test(text[span.pos - 1] ?? '');
    const touchesEnd = span.end >= text.length || newlineAt.has(span.end) || /\s/.test(text[span.end] ?? '');
    void touchesStart;
    void touchesEnd;
    if (newlines > 0) {
      out += '\n'.repeat(newlines);
    } else {
      const before = out.endsWith(' ') || out.endsWith('\n') || out.length === 0;
      const after = span.end >= text.length || /[\s;{}(),]/.test(text[span.end] ?? '');
      if (!before && !after) out += ' ';
    }
    cursor = span.end;
  }
  out += text.slice(cursor);
  return out;
}

export async function analyzeComments({ root } = {}) {
  const resolvedRoot = resolveRoot(root);
  const files = await collectFiles(resolvedRoot);
  const diagnostics = [];
  const seen = new Set();
  for (const full of files) {
    const rel = toPosix(path.relative(resolvedRoot, full));
    if (isGeneratedPath(rel)) continue;
    if (isMarkdownPath(rel)) continue;
    if (isJsonPath(rel)) continue;
    let text;
    try {
      text = await readFile(full, 'utf8');
    } catch {
      throw new Error(`cannot read ${rel}`);
    }
    let inspected;
    try {
      inspected = inspectSource(rel, text);
    } catch (error) {
      throw new Error(`cannot parse ${rel}: ${error.message}`);
    }
    if (!inspected) continue;
    for (const entry of inspected.diagnostics) {
      const key = `${rel}:${entry.line}:${entry.pos}`;
      if (seen.has(key)) continue;
      seen.add(key);
      diagnostics.push({ rule: 'NOCOMMENT', path: rel, line: entry.line, message: 'comment prohibited in Dune scope' });
    }
  }
  diagnostics.sort((a, b) => (a.path < b.path ? -1 : a.path > b.path ? 1 : a.line - b.line));
  return { diagnostics };
}

export async function fixComments({ root, dryRun } = {}) {
  const resolvedRoot = resolveRoot(root);
  const files = await collectFiles(resolvedRoot);
  const changes = [];
  for (const full of files) {
    const rel = toPosix(path.relative(resolvedRoot, full));
    if (isGeneratedPath(rel)) continue;
    if (isMarkdownPath(rel)) continue;
    if (isJsonPath(rel)) continue;
    const text = await readFile(full, 'utf8');
    let inspected;
    try {
      inspected = inspectSource(rel, text);
    } catch (error) {
      throw new Error(`cannot parse ${rel}: ${error.message}`);
    }
    if (!inspected || inspected.ranges.length === 0) continue;
    const next = applyFix(text, inspected.ranges);
    const count = inspected.ranges.length;
    if (next !== text) {
      changes.push({ path: rel, count });
      if (!dryRun) {
        const temp = await mkdtemp(path.join(tmpdir(), 'dune-comments-'));
        try {
          const staging = path.join(temp, 'fixed');
          await writeFile(staging, next, 'utf8');
          await rename(staging, full);
        } finally {
          await rm(temp, { recursive: true, force: true });
        }
      }
    }
  }
  changes.sort((a, b) => (a.path < b.path ? -1 : a.path > b.path ? 1 : 0));
  return changes;
}

function parseArgs(argv) {
  const args = { root: undefined, format: 'text', fix: false, dryRun: false };
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];
    if (token === '--root') {
      args.root = argv[index + 1];
      index += 1;
    } else if (token === '--format') {
      args.format = argv[index + 1];
      index += 1;
    } else if (token === '--fix') {
      args.fix = true;
    } else if (token === '--dry-run') {
      args.dryRun = true;
    } else if (token === '--help' || token === '-h') {
      args.help = true;
    } else {
      throw new Error(`unknown argument ${token}`);
    }
  }
  if (args.format !== 'text' && args.format !== 'json') throw new Error(`unknown format ${args.format}`);
  if (args.fix && args.dryRun) throw new Error('--fix and --dry-run are mutually exclusive');
  return args;
}

async function main(argv) {
  let args;
  try {
    args = parseArgs(argv);
  } catch (error) {
    console.error(`check-comments: ${error.message}`);
    return 2;
  }
  if (args.help) {
    console.log('usage: node dune/check-comments.mjs [--root <dir>] [--format text|json] [--fix|--dry-run]');
    return 0;
  }
  if (args.fix || args.dryRun) {
    let changes;
    try {
      changes = await fixComments({ root: args.root, dryRun: args.dryRun });
    } catch (error) {
      console.error(`check-comments: ${error.message}`);
      return 2;
    }
    const total = changes.reduce((sum, entry) => sum + entry.count, 0);
    if (args.format === 'json') {
      console.log(JSON.stringify(changes, null, 2));
    } else {
      for (const entry of changes) console.log(`${entry.path}: ${entry.count} comment(s)${args.dryRun ? ' (dry-run)' : ''}`);
    }
    console.log(args.dryRun ? `check-comments: DRY-RUN (${total} comments)` : `check-comments: FIXED (${total} comments)`);
    return 0;
  }
  let result;
  try {
    result = await analyzeComments({ root: args.root });
  } catch (error) {
    console.error(`check-comments: ${error.message}`);
    return 2;
  }
  if (args.format === 'json') {
    console.log(JSON.stringify(result.diagnostics, null, 2));
  } else if (result.diagnostics.length > 0) {
    for (const entry of result.diagnostics) console.log(`${entry.rule} ${entry.path}:${entry.line} ${entry.message}`);
  }
  if (result.diagnostics.length > 0) {
    console.log(`check-comments: FAIL (${result.diagnostics.length} diagnostics)`);
    return 1;
  }
  console.log('check-comments: OK');
  return 0;
}

const invoked = process.argv[1] ? toPosix(path.resolve(process.argv[1])) === toPosix(fileURLToPath(import.meta.url)) : false;
if (invoked) {
  const code = await main(process.argv.slice(2));
  process.exitCode = code;
}

void stat;
