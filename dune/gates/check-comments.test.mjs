import { strict as assert } from 'node:assert';
import { describe, it } from 'node:test';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import {
  analyzeComments,
  applyFix,
  fixComments,
  inspectSource,
} from './check-comments.mjs';

async function makeRoot(files) {
  const root = await mkdtemp(path.join(tmpdir(), 'dune-comments-'));
  for (const [rel, content] of Object.entries(files)) {
    const full = path.join(root, rel);
    const { mkdir } = await import('node:fs/promises');
    await mkdir(path.dirname(full), { recursive: true });
    await writeFile(full, content);
  }
  return { root, cleanup: () => rm(root, { recursive: true, force: true }) };
}

describe('check-comments', () => {
  it('ignores comment shapes inside strings templates regex and urls', () => {
    const ts = `const a = "http://x//y";\nconst r = /a\\/\\/b/;\nconst t = \`hello // not /* x */ \${1 + 2}\`;\nconst u = a + r + t;\nconsole.log(u);\n`;
    const inspected = inspectSource('frontend/src/app/core/sample.ts', ts);
    assert.deepEqual(inspected.diagnostics, []);
  });

  it('reports trailing and file-only comments including EOF without newline', () => {
    const trailing = inspectSource('frontend/src/app/core/a.ts', 'const value = 1; // end-of-file');
    assert.equal(trailing.diagnostics.length, 1);
    assert.equal(trailing.diagnostics[0].line, 1);
    const only = inspectSource('dune/tool.mjs', '// only comment file\n');
    assert.equal(only.diagnostics.length, 1);
    const two = inspectSource('dune/two.mjs', 'const a = 1; // one\n/* two */ const b = 2;\n');
    assert.equal(two.diagnostics.length, 2);
    assert.deepEqual(two.diagnostics.map((entry) => entry.line), [1, 2]);
  });

  it('finds template expression comments while ignoring literal spans', () => {
    const inspected = inspectSource('frontend/src/app/core/a.ts', 'const t = `a${1 /* real */} b`;\n');
    assert.equal(inspected.diagnostics.length, 1);
  });

  it('parses html raw text without flagging attribute lookalikes', () => {
    const inspected = inspectSource(
      'frontend/src/app/app.html',
      '<p title="<!--x-->">t</p><!-- real -->\n<script>const a = "<!--";</script>\n',
    );
    assert.equal(inspected.diagnostics.length, 1);
    assert.equal(inspected.diagnostics[0].line, 1);
  });

  it('keeps angular interpolation literals while flagging template comments', () => {
    const inspected = inspectSource(
      'frontend/src/app/tasks.html',
      '<div title="a{{ caption }}b">{{ user?.name }}@if (ready) { go }<!-- real --></div>\n',
    );
    assert.equal(inspected.diagnostics.length, 1);
    assert.equal(inspected.diagnostics[0].line, 1);
    const literal = inspectSource(
      'frontend/src/app/neg.html',
      '<p title="{{ prefix }}<!--x-->">{{ label }}</p>\n',
    );
    assert.deepEqual(literal.diagnostics, []);
  });

  it('keeps xml cdata and processing instructions while flagging comments', () => {
    const inspected = inspectSource(
      'backend/config.xml',
      '<?xml version="1.0"?><root><![CDATA[<!-- not a comment -->]]><!-- yes --><?pi data?></root>\n',
    );
    assert.equal(inspected.diagnostics.length, 1);
  });

  it('rejects malformed xml instead of reporting green', () => {
    assert.throws(() => inspectSource('backend/bad.xml', '<root><!-- unterminated'), /cannot parse/);
  });

  it('handles yaml blocks quotes flow and full-line comments', () => {
    const clean = inspectSource(
      'compose.yaml',
      'lit: |\n  # not comment\n  text # not\nkey: "v # not"\nlist: [1, 2]\n',
    );
    assert.deepEqual(clean.diagnostics, []);
    const flagged = inspectSource(
      'deploy/otel-collector-config.yaml',
      'a: 1 # one\n# full\nb: 2\n',
    );
    assert.equal(flagged.diagnostics.length, 2);
    assert.deepEqual(flagged.diagnostics.map((entry) => entry.line), [1, 2]);
  });

  it('handles css escaped strings and reports real comments', () => {
    const inspected = inspectSource(
      'frontend/src/styles.css',
      'a{content:"/*x*/"}/* real */\n',
    );
    assert.equal(inspected.diagnostics.length, 1);
    assert.equal(inspected.diagnostics[0].line, 1);
  });

  it('fix preserves literals token separation and newlines', async () => {
    const fixture = await makeRoot({
      'frontend/src/app/core/sample.ts': 'const a = 1; // gone\nconst b = "/* kept */";\n',
      'frontend/src/styles.css': 'a{content:"/*x*/"}/* gone */\n',
    });
    try {
      const before = await analyzeComments({ root: fixture.root });
      assert.equal(before.diagnostics.length, 2);
      const dry = await fixComments({ root: fixture.root, dryRun: true });
      assert.equal(dry.reduce((sum, entry) => sum + entry.count, 0), 2);
      const afterDry = await analyzeComments({ root: fixture.root });
      assert.equal(afterDry.diagnostics.length, 2);
      await fixComments({ root: fixture.root, dryRun: false });
      const after = await analyzeComments({ root: fixture.root });
      assert.deepEqual(after.diagnostics, []);
      const { readFile } = await import('node:fs/promises');
      const fixedTs = await readFile(path.join(fixture.root, 'frontend/src/app/core/sample.ts'), 'utf8');
      assert.ok(fixedTs.includes('"/* kept */"'));
      assert.ok(!fixedTs.includes('// gone'));
      const fixedCss = await readFile(path.join(fixture.root, 'frontend/src/styles.css'), 'utf8');
      assert.ok(fixedCss.includes('"/*x*/"'));
    } finally {
      await fixture.cleanup();
    }
  });

  it('applyFix keeps CRLF and token separation', () => {
    const next = applyFix('const a=1;//x\r\nconst b=2;\r\n', [{ pos: 10, end: 13 }]);
    assert.ok(next.includes('\r\n'));
    assert.ok(!next.includes('//x'));
    const joined = applyFix('const a = 1;//x\nconst b = 2;\n', [{ pos: 12, end: 15 }]);
    assert.ok(!joined.includes('//x'));
    assert.ok(joined.includes('1'));
    assert.ok(joined.includes('const b'));
  });

  it('fails parsing broken yaml instead of reporting green', async () => {
    const fixture = await makeRoot({ 'compose.yaml': 'a: [1,\n' });
    try {
      await assert.rejects(() => analyzeComments({ root: fixture.root }), /cannot parse/);
    } finally {
      await fixture.cleanup();
    }
  });

  it('accepts a bare ts-expect-error directive', () => {
    const inspected = inspectSource(
      'frontend/src/app/core/a.ts',
      'const a: number = 1;\n// @ts-expect-error\nconst b: string = a;\n',
    );
    assert.deepEqual(inspected.diagnostics, []);
  });

  it('rejects ts directives that carry prose or are not allowed', () => {
    const prose = inspectSource('frontend/src/app/core/a.ts', '// @ts-expect-error legacy typing\nconst b = 1;\n');
    assert.equal(prose.diagnostics.length, 1);
    const ignore = inspectSource('frontend/src/app/core/a.ts', '// @ts-ignore\nconst b = 1;\n');
    assert.equal(ignore.diagnostics.length, 1);
    const block = inspectSource('frontend/src/app/core/a.ts', '/* @ts-expect-error */\nconst b = 1;\n');
    assert.equal(block.diagnostics.length, 1);
  });

  it('accepts dockerfile parser directives at the top', () => {
    const inspected = inspectSource(
      'backend/Dockerfile',
      '# syntax=docker/dockerfile:1\n# escape=`\n# check=error=true\nFROM scratch\n',
    );
    assert.deepEqual(inspected.diagnostics, []);
  });

  it('rejects dockerfile directives after the top and prose comments', () => {
    const late = inspectSource('backend/Dockerfile', 'FROM scratch\n# syntax=docker/dockerfile:1\n');
    assert.deepEqual(late.diagnostics.map((entry) => entry.line), [2]);
    const afterComment = inspectSource('backend/Dockerfile', '# build stage\n# syntax=docker/dockerfile:1\nFROM scratch\n');
    assert.deepEqual(afterComment.diagnostics.map((entry) => entry.line), [1, 2]);
    const afterBlank = inspectSource('backend/Dockerfile', '\n# syntax=docker/dockerfile:1\nFROM scratch\n');
    assert.deepEqual(afterBlank.diagnostics.map((entry) => entry.line), [2]);
  });

  it('keeps directives in banned symbols as comments', () => {
    const inspected = inspectSource('dune/BannedSymbols.txt', '# syntax=docker/dockerfile:1\nT:System.Console\n');
    assert.equal(inspected.diagnostics.length, 1);
  });

  it('fix keeps allowed directives', async () => {
    const fixture = await makeRoot({
      'frontend/src/app/core/a.ts': '// @ts-expect-error\nconst b: string = 1; // note\n',
      'backend/Dockerfile': '# syntax=docker/dockerfile:1\n# note\nFROM scratch\n',
    });
    try {
      await fixComments({ root: fixture.root });
      const { readFile } = await import('node:fs/promises');
      const ts = await readFile(path.join(fixture.root, 'frontend/src/app/core/a.ts'), 'utf8');
      const docker = await readFile(path.join(fixture.root, 'backend/Dockerfile'), 'utf8');
      assert.ok(ts.startsWith('// @ts-expect-error\n'));
      assert.ok(!ts.includes('// note'));
      assert.ok(docker.startsWith('# syntax=docker/dockerfile:1\n'));
      assert.ok(!docker.includes('# note'));
      assert.deepEqual((await analyzeComments({ root: fixture.root })).diagnostics, []);
    } finally {
      await fixture.cleanup();
    }
  });
});
