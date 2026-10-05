import { strict as assert } from 'node:assert';
import { describe, it } from 'node:test';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

const run = promisify(execFile);
const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const script = path.join(scriptDir, 'check-structure.mjs');

async function makeRepo() {
  const dir = await mkdtemp(path.join(tmpdir(), 'dune-structure-'));
  async function write(rel, content = '') {
    const full = path.join(dir, rel);
    await mkdir(path.dirname(full), { recursive: true });
    await writeFile(full, content);
  }
  return { dir, write, cleanup: () => rm(dir, { recursive: true, force: true }) };
}

async function seedValid(repo) {
  for (const feature of ['Auth', 'Users', 'Tasks', 'Health']) {
    await repo.write(`backend/Features/${feature}/Tests/${feature}Tests.cs`, '// test');
  }
  await repo.write('backend/Program.cs', '// composition');
  await repo.write(
    'backend/TemplateApp.csproj',
    '<Project><ItemGroup>'
      + '<Compile Remove="Features/**/Tests/**/*.cs" />'
      + '<Compile Remove="Tests/**/*.cs" />'
      + '</ItemGroup></Project>',
  );
  await repo.write(
    'backend/Tests/TemplateApp.Tests.csproj',
    '<Project><ItemGroup><ProjectReference Include="../TemplateApp.csproj" /></ItemGroup></Project>',
  );
  await repo.write('openspec/specs/tasks/spec.md', '# tasks');
}

async function runGate(dir) {
  try {
    const { stdout } = await run(process.execPath, [script], { cwd: dir });
    return { code: 0, output: stdout };
  } catch (error) {
    return { code: error.code ?? 1, output: `${error.stdout ?? ''}${error.stderr ?? ''}` };
  }
}

describe('check-structure', () => {
  it('acepta un repo colocado y sin residuos', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      const result = await runGate(repo.dir);
      assert.equal(result.code, 0, result.output);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza tests fuera de la feature', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/Features/Tasks/Tests/TasksTests.cs', '');
      await rm(path.join(repo.dir, 'backend/Features/Auth/Tests'), { recursive: true, force: true });
      const result = await runGate(repo.dir);
      assert.notEqual(result.code, 0);
      assert.match(result.output, /tests de Auth fuera de su feature/);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza producto que compila tests', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/TemplateApp.csproj', '<Project></Project>');
      const result = await runGate(repo.dir);
      assert.notEqual(result.code, 0);
      assert.match(result.output, /no excluye tests del producto/);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza residuo Wolverine', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/Infrastructure/Composition.cs', 'using Wolverine;');
      const result = await runGate(repo.dir);
      assert.notEqual(result.code, 0);
      assert.match(result.output, /residuo Wolverine/);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza openspec duplicado y artefactos versionados', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/openspec/changes/legacy.md', 'legacy');
      await repo.write('backend/Features/Tasks/bundle.nupkg', 'binary');
      const result = await runGate(repo.dir);
      assert.notEqual(result.code, 0);
      assert.match(result.output, /openspec duplicado|artefactos versionados/);
    } finally {
      await repo.cleanup();
    }
  });
});
