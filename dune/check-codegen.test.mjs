import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { TARGETS, coverage, verdict } from './check-codegen.mjs';

const [monolith, tasks] = TARGETS;

describe('check-codegen', () => {
  it('revisa el código generado del monolito y el de Tasks', () => {
    assert.deepEqual(
      TARGETS.map((target) => target.project),
      ['backend/Hosts/Monolith/TemplateApp.csproj', 'backend/Hosts/Tasks/TemplateApp.Tasks.csproj'],
    );
  });

  it('revisa la carpeta Internal/Generated del proyecto, donde escribe codegen write', () => {
    assert.deepEqual(
      TARGETS.map((target) => target.generated),
      ['backend/Hosts/Monolith/Internal/Generated', 'backend/Hosts/Tasks/Internal/Generated'],
    );
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
