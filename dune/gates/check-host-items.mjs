import { spawnSync } from 'node:child_process';
import { realpathSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';
import { discoverHosts } from './hosts.mjs';

const RULE = 'límite de despliegue';

function ownsFeature(features) {
  return (relative) => {
    const match = /^backend\/Features\/([^/]+)\//.exec(relative);
    return match !== null && features(match[1]);
  };
}

function names(value) {
  return (value ?? '')
    .split(',')
    .map((name) => name.trim())
    .filter((name) => name.length > 0);
}

export function hostFromDeclaration(project, { DuneHostFeatures, DuneHostExcludedFeatures }) {
  const features = names(DuneHostFeatures);
  const excluded = names(DuneHostExcludedFeatures).map((name) => name.toLowerCase());
  if ((features.length > 0) === (excluded.length > 0)) {
    return {
      problem:
        features.length > 0
          ? `${RULE}: ${project} declara DuneHostFeatures y DuneHostExcludedFeatures; su .csproj declara una sola: las features que despliega o las que deja afuera`
          : `${RULE}: ${project} no declara qué features despliega; su .csproj lleva <DuneHostFeatures>A,B</DuneHostFeatures> (solo esas) o <DuneHostExcludedFeatures>A</DuneHostExcludedFeatures> (todas salvo esas)`,
    };
  }
  return features.length > 0
    ? {
        project,
        scope: `el host compila sus archivos y Features/${features.join(', Features/')} (DuneHostFeatures de su .csproj)`,
        compiles: ownsFeature((feature) => features.includes(feature)),
      }
    : {
        project,
        scope: `el host compila sus archivos y Features salvo Features/${names(DuneHostExcludedFeatures).join(', Features/')} (DuneHostExcludedFeatures de su .csproj)`,
        compiles: ownsFeature((feature) => !excluded.includes(feature.toLowerCase())),
      };
}

export function violations(host, files) {
  const own = `${path.posix.dirname(host.project)}/`;
  return files
    .map((file) => file.replaceAll('\\', '/'))
    .filter((file) => {
      if (file.toLowerCase().endsWith('tests.cs')) return true;
      return !file.startsWith(own) && !host.compiles(file);
    })
    .map((file) => `${RULE}: ${host.project} compila ${file}; ${host.scope}, sin tests, y el resto llega por backend/Platform/TemplateApp.Platform.csproj`);
}

const PLATFORM_PROJECT = 'backend/Platform/TemplateApp.Platform.csproj';
const DUNE_ANALYZERS_PROJECT = 'dune/Dune.Analyzers/Dune.Analyzers.csproj';

export function referenceViolations(host, references) {
  return references
    .map((reference) => reference.replaceAll('\\', '/'))
    .filter((reference) => reference !== PLATFORM_PROJECT && reference !== DUNE_ANALYZERS_PROJECT)
    .map((reference) => `${RULE}: ${host.project} referencia ${reference}; un host referencia solo ${PLATFORM_PROJECT} (y el analyzer de Dune que agrega Dune.props), también con ReferenceOutputAssembly="false"`);
}

function evaluatedItems(root, project, configuration) {
  const result = spawnSync('dotnet', ['msbuild', project, '-nologo', '-getItem:Compile', '-getItem:ProjectReference', '-getProperty:DuneHostFeatures', '-getProperty:DuneHostExcludedFeatures', `-p:Configuration=${configuration}`], {
    cwd: root,
    encoding: 'utf8',
    shell: process.platform === 'win32',
    maxBuffer: 64 * 1024 * 1024,
  });
  if (result.status !== 0) {
    process.stderr.write(result.stdout ?? '');
    process.stderr.write(result.stderr ?? '');
    throw new Error(`dotnet msbuild ${project} -getItem:Compile -getItem:ProjectReference terminó con ${result.status}`);
  }
  const output = JSON.parse(result.stdout);
  const items = output.Items ?? {};
  const relative = (item) => path.relative(root, realpathSync.native(item.FullPath)).split(path.sep).join('/');
  return {
    files: (items.Compile ?? []).map(relative),
    references: (items.ProjectReference ?? []).map(relative),
    declaration: output.Properties ?? {},
  };
}

function main() {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
  const { projects, problems } = discoverHosts(root);
  const failures = [...problems.map((problem) => `${RULE}: ${problem}`)];
  const counts = [];
  for (const configuration of ['Release', 'Debug']) {
    for (const project of projects) {
      const { files, references, declaration } = evaluatedItems(root, project, configuration);
      const host = hostFromDeclaration(project, declaration);
      if (host.problem) {
        failures.push(host.problem);
        continue;
      }
      counts.push(`${host.project} en ${configuration}: ${files.length} archivos y ${references.length} referencias`);
      if (files.length === 0) failures.push(`${RULE}: ${host.project} no compila ningún archivo en ${configuration}; el gate no tiene qué revisar`);
      failures.push(...violations(host, files), ...referenceViolations(host, references));
    }
  }
  console.log(`check-host-items: ${failures.length === 0 ? 'PASS' : 'FAIL'} (${counts.join(', ')})`);
  for (const failure of failures) console.log(`- ${failure}`);
  if (failures.length > 0) process.exit(1);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) main();
