import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { TARGETS, verdict } from './check-codegen.mjs';

const [monolith, tasks] = TARGETS;

describe('check-codegen', () => {
  it('revisa el código generado del monolito y el de Tasks', () => {
    assert.deepEqual(
      TARGETS.map((target) => target.project),
      ['backend/TemplateApp.csproj', 'backend/Hosts/Tasks/TemplateApp.Tasks.csproj'],
    );
  });

  it('acepta el código generado sin cambios después de codegen write', () => {
    assert.equal(verdict(monolith, '').ok, true);
    assert.equal(verdict(monolith, '\n').ok, true);
    assert.equal(verdict(monolith, 'A  backend/Internal/Generated/WolverineHandlers/AccountDeletedInfoHandler451093863.cs\n').ok, true);
    assert.equal(verdict(tasks, 'A  backend/Hosts/Tasks/Internal/Generated/WolverineHandlers/CreateTaskCommandHandler776868131.cs\n').ok, true);
  });

  for (const [name, target, porcelain] of [
    ['un handler que codegen write reescribe', monolith, ' M backend/Internal/Generated/WolverineHandlers/AccountDeletedInfoHandler451093863.cs\n'],
    ['un handler nuevo sin versionar', monolith, '?? backend/Internal/Generated/WolverineHandlers/NewMessageHandler1.cs\n'],
    ['un handler versionado que codegen write ya no genera', monolith, ' D backend/Internal/Generated/WolverineHandlers/OldMessageHandler1.cs\n'],
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
