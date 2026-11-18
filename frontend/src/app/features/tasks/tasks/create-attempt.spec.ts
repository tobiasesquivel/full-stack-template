import { nextCreateAttempt } from './create-attempt';

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;

describe('nextCreateAttempt', () => {
  it('starts a new attempt with a v4 UUID key when there is none', () => {
    const attempt = nextCreateAttempt(null, { title: 'Comprar pan', projectId: null });
    expect(attempt.request).toEqual({ title: 'Comprar pan', projectId: null });
    expect(attempt.key).toMatch(UUID);
  });

  it('keeps the key when the same title and project are sent again', () => {
    const request = { title: 'Comprar pan', projectId: 'p-1' };
    const first = nextCreateAttempt(null, request);
    expect(nextCreateAttempt(first, { ...request })).toEqual(first);
  });

  it('uses a new key when the title changed', () => {
    const first = nextCreateAttempt(null, { title: 'Comprar pan', projectId: null });
    const second = nextCreateAttempt(first, { title: 'Comprar leche', projectId: null });
    expect(second.request.title).toBe('Comprar leche');
    expect(second.key).toMatch(UUID);
    expect(second.key).not.toBe(first.key);
  });

  it('uses a new key when the project changed', () => {
    const first = nextCreateAttempt(null, { title: 'Comprar pan', projectId: null });
    const second = nextCreateAttempt(first, { title: 'Comprar pan', projectId: 'p-1' });
    expect(second.request.projectId).toBe('p-1');
    expect(second.key).not.toBe(first.key);
  });
});
