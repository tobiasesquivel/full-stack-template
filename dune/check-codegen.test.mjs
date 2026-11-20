import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { verdict } from './check-codegen.mjs';

describe('check-codegen', () => {
  it('acepta el código generado sin cambios después de codegen write', () => {
    assert.equal(verdict('').ok, true);
    assert.equal(verdict('\n').ok, true);
    assert.equal(verdict('A  backend/Internal/Generated/WolverineHandlers/AccountDeletedInfoHandler451093863.cs\n').ok, true);
  });

  for (const [name, porcelain] of [
    ['un handler que codegen write reescribe', ' M backend/Internal/Generated/WolverineHandlers/AccountDeletedInfoHandler451093863.cs\n'],
    ['un handler nuevo sin versionar', '?? backend/Internal/Generated/WolverineHandlers/NewMessageHandler1.cs\n'],
    ['un handler versionado que codegen write ya no genera', ' D backend/Internal/Generated/WolverineHandlers/OldMessageHandler1.cs\n'],
  ]) {
    it(`rechaza ${name}`, () => {
      const result = verdict(porcelain);
      assert.equal(result.ok, false);
      assert.match(result.message, /no coincide con lo que genera codegen write/);
    });
  }
});
