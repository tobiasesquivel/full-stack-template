import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import { discoverHosts, featureFolders } from './hosts.mjs';

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
  it('encuentra los hosts del repo en backend/Hosts, sin problemas', () => {
    const { projects, problems } = discoverHosts(ROOT);
    assert.deepEqual(problems, []);
    assert.ok(projects.includes('backend/Hosts/Monolith/TemplateApp.csproj'), projects.join(', '));
    assert.ok(projects.includes('backend/Hosts/Tasks/TemplateApp.Tasks.csproj'), projects.join(', '));
    assert.deepEqual(projects, [...projects].sort());
  });

  it('lista las carpetas de backend/Features del repo', () => {
    const features = featureFolders(ROOT);
    assert.ok(features.includes('Tasks') && features.includes('Health'), features.join(', '));
  });

  it('suma un host inventado sin editar ninguna lista', () => {
    withRepo({ Monolith: ['TemplateApp.csproj'], Billing: ['Acme.Billing.csproj', 'appsettings.json'] }, (root) => {
      assert.deepEqual(discoverHosts(root).projects, ['backend/Hosts/Billing/Acme.Billing.csproj', 'backend/Hosts/Monolith/TemplateApp.csproj']);
    });
  });

  it('rechaza un .csproj suelto en backend/Hosts, fuera de la carpeta de un host', () => {
    withRepo({ Monolith: ['TemplateApp.csproj'] }, (root) => {
      writeFileSync(path.join(root, 'backend/Hosts/Billing.csproj'), '<Project />');
      const { projects, problems } = discoverHosts(root);
      assert.deepEqual(projects, ['backend/Hosts/Monolith/TemplateApp.csproj']);
      assert.deepEqual(problems, ['backend/Hosts/Billing.csproj está suelto; cada host vive en backend/Hosts/<Host>/, con su .csproj en la raíz de esa carpeta']);
    });
  });

  it('rechaza la carpeta de hosts escrita con otras mayúsculas', () => {
    const root = mkdtempSync(path.join(os.tmpdir(), 'dune-hosts-'));
    try {
      mkdirSync(path.join(root, 'backend/hosts/Billing'), { recursive: true });
      writeFileSync(path.join(root, 'backend/hosts/Billing/Acme.Billing.csproj'), '<Project />');
      const { projects, problems } = discoverHosts(root);
      assert.deepEqual(projects, []);
      assert.ok(problems.includes('backend/hosts: la carpeta de hosts se escribe backend/Hosts; con otra grafía, check-host-items y check-codegen no la recorren'), problems.join('\n'));
    } finally {
      rmSync(root, { recursive: true, force: true });
    }
  });

  it('rechaza un host que es un enlace a otra carpeta, aunque la carpeta tenga un .csproj', () => {
    withRepo({ Monolith: ['TemplateApp.csproj'] }, (root) => {
      const outside = path.join(root, 'outside');
      mkdirSync(outside);
      writeFileSync(path.join(outside, 'TemplateApp.Tasks.csproj'), '<Project />');
      symlinkSync(outside, path.join(root, 'backend/Hosts/Tasks'), 'junction');
      const { projects, problems } = discoverHosts(root);
      assert.deepEqual(projects, ['backend/Hosts/Monolith/TemplateApp.csproj']);
      assert.equal(problems.length, 1, problems.join('\n'));
      assert.ok(problems[0].startsWith('backend/Hosts/Tasks es un enlace'), problems[0]);
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
