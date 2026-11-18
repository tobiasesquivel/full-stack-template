import { nextCreateAttempt } from './create-attempt';

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

describe('nextCreateAttempt', () => {
  it('starts a new attempt with a v4 UUID key when there is none', () => {
    const attempt = nextCreateAttempt(null, { name: 'Hogar' });
    expect(attempt.request).toEqual({ name: 'Hogar' });
    expect(attempt.key).toMatch(UUID);
  });

  it('keeps the key when the same name is sent again', () => {
    const first = nextCreateAttempt(null, { name: 'Hogar' });
    expect(nextCreateAttempt(first, { name: 'Hogar' })).toEqual(first);
  });

  it('uses a new key when the name changed', () => {
    const first = nextCreateAttempt(null, { name: 'Hogar' });
    const second = nextCreateAttempt(first, { name: 'Trabajo' });
    expect(second.request.name).toBe('Trabajo');
    expect(second.key).not.toBe(first.key);
  });
});
