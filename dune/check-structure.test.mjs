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
  async function git(args) {
    await run('git', args, { cwd: dir });
  }
  return { dir, write, git, cleanup: () => rm(dir, { recursive: true, force: true }) };
}

async function seedValid(repo) {
  for (const feature of ['Auth', 'Users', 'Tasks', 'Health']) {
    await repo.write(`backend/Features/${feature}/Tests/${feature}Tests.cs`, ' ');
  }
  await repo.write('backend/Program.cs', ' ');
  await repo.write(
    'backend/TemplateApp.csproj',
    '<Project><ItemGroup>'
      + '<Compile Remove="Features/**/Tests/**/*.cs" />'
      + '<Compile Remove="Tests/**/*.cs" />'
      + '</ItemGroup></Project>',
  );
  await repo.write(
    'backend/Tests/TemplateApp.Tests.csproj',
    '<Project><ItemGroup><ProjectReference Include="../TemplateApp.csproj" />'
      + '<Compile Include="..\\Features\\*\\Tests\\**\\*.cs" />'
      + '</ItemGroup></Project>',
  );
  await repo.git(['init']);
  await repo.git(['add', '.']);
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
      assert.equal(result.code, 1);
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
      assert.equal(result.code, 1);
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
      assert.equal(result.code, 1);
      assert.match(result.output, /residuo Wolverine/);
    } finally {
      await repo.cleanup();
    }
  });

  it('acepta cache Angular ignorada', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('.gitignore', '*.db\n*.db-shm\n*.db-wal\n');
      await repo.write('frontend/.angular/cache/22.2.1/frontend/angular-compiler.db', 'cache');
      const result = await runGate(repo.dir);
      assert.equal(result.code, 0, result.output);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza db ignorada con git add forzado', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('.gitignore', '*.db\n*.db-shm\n*.db-wal\n');
      await repo.git(['add', '.gitignore']);
      await repo.write('frontend/.angular/cache/22.2.1/frontend/angular-compiler.db', 'cache');
      await repo.git(['add', '-f', 'frontend/.angular/cache/22.2.1/frontend/angular-compiler.db']);
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /artefactos versionados/);
      assert.match(result.output, /angular-compiler\.db/);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza git anidado como directorio', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/Features/Tasks/vendor/lib/.git/HEAD', 'ref: refs/heads/main');
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /git anidado/);
      assert.match(result.output, /vendor\/lib\/\.git/);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza git anidado como archivo', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/Features/Tasks/vendor/.git', 'gitdir: ../../elsewhere');
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /git anidado/);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza bin versionado', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/Features/Tasks/bin/Debug/output.dll', 'binary');
      await repo.git(['add', '-f', 'backend/Features/Tasks/bin/Debug/output.dll']);
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /artefactos versionados/);
      assert.match(result.output, /bin\/Debug\/output\.dll/);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza artefacto borrado pero indexado', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/Features/Tasks/bundle.nupkg', 'binary');
      await repo.git(['add', 'backend/Features/Tasks/bundle.nupkg']);
      await rm(path.join(repo.dir, 'backend/Features/Tasks/bundle.nupkg'), { force: true });
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /artefactos versionados/);
      assert.match(result.output, /bundle\.nupkg/);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza artefacto rastreado', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/Features/Tasks/bundle.nupkg', 'binary');
      await repo.git(['add', 'backend/Features/Tasks/bundle.nupkg']);
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /artefactos versionados/);
      assert.match(result.output, /bundle\.nupkg/);
    } finally {
      await repo.cleanup();
    }
  });

  it('acepta una feature nueva con Tests colocados', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/Features/Billing/Tests/BillingTests.cs', ' ');
      const result = await runGate(repo.dir);
      assert.equal(result.code, 0, result.output);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza una feature nueva sin Tests', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write('backend/Features/Billing/BillingModule.cs', ' ');
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /tests de Billing fuera de su feature/);
    } finally {
      await repo.cleanup();
    }
  });

  it('rechaza inventario de tests sin glob de Features', async () => {
    const repo = await makeRepo();
    try {
      await seedValid(repo);
      await repo.write(
        'backend/Tests/TemplateApp.Tests.csproj',
        '<Project><ItemGroup></ItemGroup></Project>',
      );
      const result = await runGate(repo.dir);
      assert.equal(result.code, 1);
      assert.match(result.output, /no referencia el producto/);
    } finally {
      await repo.cleanup();
    }
  });
});
