import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { coverage, solutionProblems, solutionsIn, targetsFor, verdict } from './check-codegen.mjs';
import { discoverHosts } from './hosts.mjs';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const [monolith, tasks] = targetsFor(['backend/Hosts/Monolith/TemplateApp.csproj', 'backend/Hosts/Tasks/TemplateApp.Tasks.csproj']);

describe('check-codegen', () => {
  it('revisa la carpeta Internal/Generated de cada host que encuentra en backend/Hosts', () => {
    const { projects } = discoverHosts(ROOT);
    assert.ok(projects.length >= 2, projects.join(', '));
    for (const target of targetsFor(projects)) {
      assert.equal(target.generated, `${path.posix.dirname(target.project)}/Internal/Generated`);
    }
  });

  it('pide que cada host esté en la solución del repo', () => {
    const solution = { name: 'TemplateApp.slnx', text: '<Solution>\n  <Project Path="backend/Hosts/Monolith/TemplateApp.csproj" />\n</Solution>\n' };
    assert.deepEqual(solutionProblems(['backend/Hosts/Monolith/TemplateApp.csproj'], [solution]), []);
    const [problem, ...rest] = solutionProblems(['backend/Hosts/Monolith/TemplateApp.csproj', 'backend/Hosts/Billing/Acme.Billing.csproj'], [solution]);
    assert.deepEqual(rest, []);
    assert.match(problem, /backend\/Hosts\/Billing\/Acme\.Billing\.csproj no está en TemplateApp\.slnx/);
    assert.equal(solutionProblems([], []).length, 1);
  });

  it('el repo tiene todos sus hosts en la solución', () => {
    assert.deepEqual(solutionProblems(discoverHosts(ROOT).projects, solutionsIn(ROOT)), []);
  });

  it('revisa un host inventado sin editar el gate', () => {
    assert.deepEqual(targetsFor(['backend/Hosts/Billing/Acme.Billing.csproj']), [
      { project: 'backend/Hosts/Billing/Acme.Billing.csproj', generated: 'backend/Hosts/Billing/Internal/Generated' },
    ]);
  });

  it('acepta el código generado sin cambios después de codegen write', () => {
    assert.equal(verdict(monolith, '').ok, true);
    assert.equal(verdict(monolith, '\n').ok, true);
    assert.equal(verdict(monolith, 'A  backend/Hosts/Monolith/Internal/Generated/WolverineHandlers/AccountDeletedInfoHandler451093863.cs\n').ok, true);
    assert.equal(verdict(tasks, 'A  backend/Hosts/Tasks/Internal/Generated/WolverineHandlers/CreateTaskCommandHandler776868131.cs\n').ok, true);
  });

  it('acepta una carpeta con código generado versionado', () => {
    assert.equal(coverage(monolith, 'backend/Hosts/Monolith/Internal/Generated/WolverineHandlers/GeneratedHandlerRegistry.cs\n').ok, true);
  });

  it('rechaza una carpeta sin código generado versionado, porque codegen write escribe en otra', () => {
    const result = coverage({ ...monolith, generated: 'backend/Internal/Generated' }, '\n');
    assert.equal(result.ok, false);
    assert.match(result.message, /backend\/Internal\/Generated no tiene código generado versionado/);
  });

  for (const [name, target, porcelain] of [
    ['un handler que codegen write reescribe', monolith, ' M backend/Hosts/Monolith/Internal/Generated/WolverineHandlers/AccountDeletedInfoHandler451093863.cs\n'],
    ['un handler nuevo sin versionar', monolith, '?? backend/Hosts/Monolith/Internal/Generated/WolverineHandlers/NewMessageHandler1.cs\n'],
    ['un handler versionado que codegen write ya no genera', monolith, ' D backend/Hosts/Monolith/Internal/Generated/WolverineHandlers/OldMessageHandler1.cs\n'],
    ['un handler de Tasks que codegen write reescribe', tasks, ' M backend/Hosts/Tasks/Internal/Generated/WolverineHandlers/CreateTaskCommandHandler776868131.cs\n'],
    ['un handler de Tasks nuevo sin versionar', tasks, '?? backend/Hosts/Tasks/Internal/Generated/WolverineHandlers/NewCommandHandler1.cs\n'],
  ]) {
    it(`rechaza ${name}`, () => {
      const result = verdict(target, porcelain);
      assert.equal(result.ok, false);
      assert.match(result.message, /no coincide con lo que genera codegen write/);
      assert.ok(result.message.includes(target.project), result.message);
    });
  }
});
