import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { after, describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const PROBE = `dune-props-probe-${process.pid}`;
const created = [];

function probe(folder) {
  const directory = path.join(ROOT, folder, PROBE);
  mkdirSync(directory, { recursive: true });
  created.push(directory);
  const project = path.join(directory, 'Acme.Probe.csproj');
  writeFileSync(project, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n');
  return path.relative(ROOT, project).split(path.sep).join('/');
}

function dune(project) {
  const result = spawnSync('dotnet', ['msbuild', project, '-nologo', '-getProperty:DuneApplies', '-getProperty:WarningsAsErrors', '-getItem:ProjectReference'], {
    cwd: ROOT,
    encoding: 'utf8',
    shell: process.platform === 'win32',
  });
  assert.equal(result.status, 0, `${result.stdout}${result.stderr}`);
  const output = JSON.parse(result.stdout);
  const analyzers = (output.Items?.ProjectReference ?? [])
    .filter((item) => item.OutputItemType === 'Analyzer')
    .map((item) => item.Identity.replaceAll('\\', '/'));
  return {
    applies: output.Properties.DuneApplies === 'true',
    errors: output.Properties.WarningsAsErrors.includes('DUNE0001'),
    analyzer: analyzers.some((reference) => reference.endsWith('Dune.Analyzers/Dune.Analyzers.csproj')),
  };
}

after(() => {
  for (const directory of created) rmSync(directory, { recursive: true, force: true });
});

describe('Dune.props', () => {
  for (const project of [
    'backend/Hosts/Monolith/TemplateApp.csproj',
    'backend/Hosts/Tasks/TemplateApp.Tasks.csproj',
    'backend/Platform/TemplateApp.Platform.csproj',
    'backend/Tests/TemplateApp.Tests.csproj',
    'gateway/TemplateApp.Gateway.csproj',
  ]) {
    it(`carga Dune en ${project}`, () => {
      assert.deepEqual(dune(project), { applies: true, errors: true, analyzer: true });
    });
  }

  for (const folder of ['backend', 'backend/Hosts', 'gateway']) {
    it(`carga Dune en un proyecto con otro nombre dentro de ${folder}/`, () => {
      assert.deepEqual(dune(probe(folder)), { applies: true, errors: true, analyzer: true });
    });
  }

  for (const project of ['dune/Dune.Analyzers/Dune.Analyzers.csproj', 'dune/Dune.Analyzers.Tests/Dune.Analyzers.Tests.csproj', 'dune/Dune.CommentVerifier/Dune.CommentVerifier.csproj']) {
    it(`no carga Dune en ${project}`, () => {
      assert.deepEqual(dune(project), { applies: false, errors: false, analyzer: false });
    });
  }

  for (const folder of ['dune', 'backendx']) {
    it(`no carga Dune en un proyecto dentro de ${folder}/`, () => {
      assert.deepEqual(dune(probe(folder)), { applies: false, errors: false, analyzer: false });
    });
  }
});
