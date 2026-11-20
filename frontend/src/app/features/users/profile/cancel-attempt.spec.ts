import { nextCancelAttempt } from './cancel-attempt';

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

describe('nextCancelAttempt', () => {
  it('starts a new attempt with a v4 UUID key when there is none', () => {
    const attempt = nextCancelAttempt(null, 'closure-1');
    expect(attempt.closureId).toBe('closure-1');
    expect(attempt.key).toMatch(UUID);
  });

  it('keeps the key when the same closure is cancelled again', () => {
    const first = nextCancelAttempt(null, 'closure-1');
    expect(nextCancelAttempt(first, 'closure-1')).toEqual(first);
  });

  it('uses a new key for another closure', () => {
    const first = nextCancelAttempt(null, 'closure-1');
    const second = nextCancelAttempt(first, 'closure-2');
    expect(second.closureId).toBe('closure-2');
    expect(second.key).not.toBe(first.key);
  });
});
