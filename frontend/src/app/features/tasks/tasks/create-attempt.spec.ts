import { nextCreateAttempt } from './create-attempt';

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

describe('nextCreateAttempt', () => {
  it('starts a new attempt with a v4 UUID key when there is none', () => {
    const attempt = nextCreateAttempt(null, 'Comprar pan');
    expect(attempt.title).toBe('Comprar pan');
    expect(attempt.key).toMatch(UUID);
  });

  it('keeps the key when the same title is sent again', () => {
    const first = nextCreateAttempt(null, 'Comprar pan');
    expect(nextCreateAttempt(first, 'Comprar pan')).toEqual(first);
  });

  it('uses a new key when the title changed', () => {
    const first = nextCreateAttempt(null, 'Comprar pan');
    const second = nextCreateAttempt(first, 'Comprar leche');
    expect(second.title).toBe('Comprar leche');
    expect(second.key).toMatch(UUID);
    expect(second.key).not.toBe(first.key);
  });
});
