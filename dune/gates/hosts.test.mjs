import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import { discoverHosts } from './hosts.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');

function withRepo(hosts, run) {
  const root = mkdtempSync(path.join(os.tmpdir(), 'dune-hosts-'));
  try {
    for (const [folder, files] of Object.entries(hosts)) {
      mkdirSync(path.join(root, 'backend/Hosts', folder), { recursive: true });
      for (const file of files) writeFileSync(path.join(root, 'backend/Hosts', folder, file), '<Project />');
    }
    mkdirSync(path.join(root, 'backend/Hosts'), { recursive: true });
    return run(root);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

describe('hosts', () => {
  it('encuentra los hosts del repo en backend/Hosts, ordenados', () => {
    assert.deepEqual(discoverHosts(ROOT), {
      projects: ['backend/Hosts/Monolith/TemplateApp.csproj', 'backend/Hosts/Tasks/TemplateApp.Tasks.csproj'],
      problems: [],
    });
  });

  it('suma un host inventado sin editar ninguna lista', () => {
    withRepo({ Monolith: ['TemplateApp.csproj'], Billing: ['Acme.Billing.csproj', 'appsettings.json'] }, (root) => {
      assert.deepEqual(discoverHosts(root).projects, ['backend/Hosts/Billing/Acme.Billing.csproj', 'backend/Hosts/Monolith/TemplateApp.csproj']);
    });
  });

  for (const [name, hosts, expected] of [
    ['una carpeta sin .csproj', { Billing: ['Program.cs'] }, 'backend/Hosts/Billing tiene 0 .csproj'],
    ['una carpeta con dos .csproj', { Billing: ['A.csproj', 'B.csproj'] }, 'backend/Hosts/Billing tiene 2 .csproj'],
    ['un host con un nombre que no se puede pasar a la shell', { 'Bill&ing': ['A.csproj'] }, 'backend/Hosts/Bill&ing: el nombre de un host'],
    ['un .csproj con un nombre que no se puede pasar a la shell', { Billing: ['A b.csproj'] }, 'un .csproj con un nombre que no se puede pasar a dotnet (A b.csproj)'],
    ['backend/Hosts vacío', {}, 'backend/Hosts no tiene hosts'],
  ]) {
    it(`rechaza ${name}`, () => {
      withRepo(hosts, (root) => {
        const { projects, problems } = discoverHosts(root);
        assert.deepEqual(projects, []);
        assert.equal(problems.length, 1, problems.join('\n'));
        assert.ok(problems[0].includes(expected), problems[0]);
      });
    });
  }
});
