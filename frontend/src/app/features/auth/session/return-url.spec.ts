import { returnTarget } from './return-url';

describe('returnTarget', () => {
  it('keeps an internal path with its query and fragment', () => {
    expect(returnTarget('/profile')).toBe('/profile');
    expect(returnTarget('/tasks?page=2#top')).toBe('/tasks?page=2#top');
  });

  it('falls back to /tasks without a value', () => {
    expect(returnTarget(null)).toBe('/tasks');
    expect(returnTarget('')).toBe('/tasks');
  });

  it('rejects values that leave the site', () => {
    expect(returnTarget('//evil.example')).toBe('/tasks');
    expect(returnTarget('/\\evil.example')).toBe('/tasks');
    expect(returnTarget('https://evil.example/tasks')).toBe('/tasks');
    expect(returnTarget('javascript:alert(1)')).toBe('/tasks');
    expect(returnTarget('tasks')).toBe('/tasks');
  });
});
