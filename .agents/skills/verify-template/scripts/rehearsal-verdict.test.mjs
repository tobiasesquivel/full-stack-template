import { strict as assert } from 'node:assert';
import { describe, it } from 'node:test';
import { stageVerdict } from './rehearsal-verdict.mjs';

const check = (name, release, ok) => ({ name, release, ok });
const healthy = [
  check('start', 'next', true),
  check('signIn', 'next', true),
  check('readiness', 'previous', true),
  check('readiness', 'next', true),
  check('writes previous to next', 'next', true),
  check('reads', 'previous', true),
  check('reads', 'next', true),
];
const previousBroken = healthy.map(item => (item.name === 'writes previous to next' ? check(item.name, 'previous', false) : item));
const nextBroken = healthy.map(item => (item.name === 'reads' && item.release === 'next' ? check(item.name, 'next', false) : item));

describe('stageVerdict', () => {
  it('passes a healthy stage that must pass', () => {
    assert.deepEqual(stageVerdict('pass', healthy), { ok: true, failedBy: [], passed: true });
  });

  it('fails a stage that must pass when either release fails', () => {
    assert.equal(stageVerdict('pass', previousBroken).passed, false);
    assert.equal(stageVerdict('pass', nextBroken).passed, false);
  });

  it('passes a negative control only when the previous release fails and the new one works', () => {
    assert.deepEqual(stageVerdict('fail', previousBroken), { ok: false, failedBy: ['previous'], passed: true });
  });

  it('fails a negative control whose stage does not fail', () => {
    assert.equal(stageVerdict('fail', healthy).passed, false);
  });

  it('fails a negative control when the new release fails and the previous one works', () => {
    assert.deepEqual(stageVerdict('fail', nextBroken), { ok: false, failedBy: ['next'], passed: false });
  });

  it('fails a negative control when the new release fails too', () => {
    const both = previousBroken.map(item => (item.name === 'reads' && item.release === 'next' ? check(item.name, 'next', false) : item));
    assert.equal(stageVerdict('fail', both).passed, false);
  });

  it('fails a negative control when a failure belongs to neither release', () => {
    assert.equal(stageVerdict('fail', [...previousBroken, check('accountDeletion', 'rehearsal', false)]).passed, false);
  });

  it('fails a negative control when the new release did not start', () => {
    assert.equal(stageVerdict('fail', [check('start', 'next', false)]).passed, false);
  });
});
