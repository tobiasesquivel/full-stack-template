import { closureNotice } from './closure-notice';

describe('closureNotice', () => {
  const rejected = {
    closureId: '0196a3f2-0000-7000-8000-000000000001',
    outcome: 'rejected' as const,
    decidedAt: '2026-11-20T10:00:00Z',
  };

  it('says the account is closing while the closure runs', () => {
    expect(closureNotice({ accountStatus: 'closing', lastClosure: null })).toBe(
      'Tu cuenta se está cerrando. Mientras tanto no puedes crear tareas nuevas.',
    );
  });

  it('explains a closure rejected because the account is the last SuperAdmin', () => {
    expect(
      closureNotice({
        accountStatus: 'active',
        lastClosure: { ...rejected, reason: 'Auth.LastSuperAdmin' },
      }),
    ).toBe('No se cerró tu cuenta: es la única cuenta con permiso para administrar usuarios.');
  });

  it('falls back to a generic line for any other rejection', () => {
    expect(
      closureNotice({
        accountStatus: 'active',
        lastClosure: { ...rejected, reason: 'Auth.Other' },
      }),
    ).toBe('No se cerró tu cuenta. Tus datos siguen igual.');
  });

  it('shows nothing for an active account without closures', () => {
    expect(closureNotice({ accountStatus: 'active', lastClosure: null })).toBeNull();
  });
});
