// comentario plantado para la prueba del PR 101
import { readFileSync, readdirSync, writeFileSync, existsSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

export const SLOW_CLASS_SECONDS = 60;

export function parseClassList(text) {
  return text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => /^[A-Za-z_][\w]*(\.[A-Za-z_][\w+]*)+$/.test(line));
}

export function parseFullListing(text) {
  const counts = new Map();
  for (const match of text.matchAll(/^\s*Test method:\s+(\S+)\s*$/gm)) {
    counts.set(match[1], (counts.get(match[1]) ?? 0) + 1);
  }
  return counts;
}

function attributes(tag) {
  const values = {};
  for (const match of tag.matchAll(/([\w-]+)="([^"]*)"/g)) values[match[1]] = match[2];
  return values;
}

function unescapeXml(value) {
  return value
    .replaceAll('&lt;', '<')
    .replaceAll('&gt;', '>')
    .replaceAll('&quot;', '"')
    .replaceAll('&apos;', "'")
    .replaceAll('&amp;', '&');
}

export function parseXunitXml(text) {
  const tests = [...text.matchAll(/<test\s[^>]*>/g)].map((match) => {
    const values = attributes(match[0]);
    return {
      method: `${unescapeXml(values.type)}.${unescapeXml(values.method)}`,
      result: values.result,
      seconds: Number(values.time),
    };
  });
  const classes = new Map();
  for (const match of text.matchAll(/<collection\s[^>]*>/g)) {
    const values = attributes(match[0]);
    const name = /^Test collection for (\S+)/.exec(unescapeXml(values.name))?.[1];
    if (name) classes.set(name, Number(values.time));
  }
  return { tests, classes };
}

export function plan(classes, timings, shardCount) {
  const known = classes.map((name) => timings[name]).filter((seconds) => Number.isFinite(seconds)).sort((a, b) => a - b);
  const fallback = known.length ? known[Math.floor(known.length / 2)] : 1;
  const weighted = classes
    .map((name) => ({ name, seconds: Number.isFinite(timings[name]) ? timings[name] : fallback }))
    .sort((a, b) => b.seconds - a.seconds || a.name.localeCompare(b.name));
  const shards = Array.from({ length: shardCount }, (_, index) => ({ index: index + 1, classes: [], seconds: 0 }));
  for (const entry of weighted) {
    const target = shards.reduce((lightest, shard) => (shard.seconds < lightest.seconds ? shard : lightest));
    target.classes.push(entry.name);
    target.seconds += entry.seconds;
  }
  return shards
    .filter((shard) => shard.classes.length > 0)
    .map((shard) => ({
      index: shard.index,
      seconds: Math.round(shard.seconds),
      classes: shard.classes.length,
      filter: shard.classes.map((name) => `--filter-class ${name}`).join(' '),
    }));
}

export function verify(listing, results) {
  const ran = new Map();
  const failed = [];
  for (const test of results.flatMap((result) => result.tests)) {
    ran.set(test.method, (ran.get(test.method) ?? 0) + 1);
    if (test.result !== 'Pass') failed.push(`${test.method}: ${test.result}`);
  }
  const problems = [];
  for (const [method, expected] of listing) {
    const actual = ran.get(method) ?? 0;
    if (actual < expected) problems.push(`no corrió: ${method} (${actual} de ${expected})`);
    if (actual > expected) problems.push(`corrió de más: ${method} (${actual} de ${expected})`);
  }
  for (const method of ran.keys()) {
    if (!listing.has(method)) problems.push(`corrió sin estar en la lista: ${method}`);
  }
  for (const failure of failed) problems.push(`no pasó: ${failure}`);
  const listed = [...listing.values()].reduce((sum, count) => sum + count, 0);
  const total = [...ran.values()].reduce((sum, count) => sum + count, 0);
  return { listed, total, problems };
}

export function classTimings(results) {
  const timings = {};
  for (const result of results) {
    for (const [name, seconds] of result.classes) timings[name] = Math.round(seconds * 10) / 10;
  }
  return Object.fromEntries(Object.entries(timings).sort(([a], [b]) => a.localeCompare(b)));
}

function option(args, name) {
  const index = args.indexOf(name);
  return index >= 0 ? args[index + 1] : undefined;
}

function readResults(directory) {
  return readdirSync(directory, { recursive: true })
    .filter((file) => String(file).endsWith('.xml'))
    .map((file) => parseXunitXml(readFileSync(path.join(directory, String(file)), 'utf8')));
}

function main(args) {
  const [command] = args;
  if (command === 'plan') {
    const classes = parseClassList(readFileSync(option(args, '--classes'), 'utf8'));
    const timingsFile = option(args, '--timings');
    const timings = timingsFile && existsSync(timingsFile) ? JSON.parse(readFileSync(timingsFile, 'utf8')) : {};
    const shards = plan(classes, timings, Number(option(args, '--shards')));
    const unknown = classes.filter((name) => !Number.isFinite(timings[name]));
    console.error(`plan: ${classes.length} clases en ${shards.length} shards; ${unknown.length} sin tiempo registrado${unknown.length ? `: ${unknown.join(', ')}` : ''}`);
    for (const shard of shards) console.error(`shard ${shard.index}: ${shard.classes} clases, ${shard.seconds} s estimados`);
    process.stdout.write(JSON.stringify(shards));
    return 0;
  }
  if (command === 'verify') {
    const listing = parseFullListing(readFileSync(option(args, '--listing'), 'utf8'));
    const results = readResults(option(args, '--results'));
    const { listed, total, problems } = verify(listing, results);
    const timings = classTimings(results);
    const timingsOut = option(args, '--timings-out');
    if (timingsOut) writeFileSync(timingsOut, `${JSON.stringify(timings, null, 2)}\n`);
    for (const [name, seconds] of Object.entries(timings)) {
      if (seconds > SLOW_CLASS_SECONDS) console.log(`::warning::${name} tardó ${seconds} s, más de ${SLOW_CLASS_SECONDS} s: partila en clases más chicas`);
    }
    console.log(`verify: ${results.length} shards, ${total} tests corridos, ${listed} en la lista del ensamblado sin repartir`);
    for (const problem of problems) console.log(`verify: ${problem}`);
    console.log(`verify: ${problems.length === 0 && listed > 0 ? 'PASS' : 'FAIL'}`);
    return problems.length === 0 && listed > 0 ? 0 : 1;
  }
  console.error('uso: backend-test-shards.mjs plan --classes <archivo> --timings <json> --shards <n> | verify --listing <archivo> --results <carpeta> [--timings-out <json>]');
  return 2;
}

if (import.meta.url === pathToFileURL(process.argv[1]).href) process.exitCode = main(process.argv.slice(2));
