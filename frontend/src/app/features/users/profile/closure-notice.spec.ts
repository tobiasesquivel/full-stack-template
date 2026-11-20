import { closureNotice } from './closure-notice';

describe('closureNotice', () => {
  const active = { accountStatus: 'active' as const, closesAt: null, timezone: 'UTC' };
  const rejected = {
    closureId: '0196a3f2-0000-7000-8000-000000000001',
    outcome: 'rejected' as const,
    decidedAt: '2026-11-20T10:00:00Z',
  };

  it('says when a scheduled closure happens, in the time zone of the profile', () => {
    expect(
      closureNotice({
        accountStatus: 'scheduled',
        closesAt: '2026-12-05T02:00:00Z',
        timezone: 'America/Argentina/Buenos_Aires',
        lastClosure: null,
      }),
    ).toBe(
      'Tu cuenta se cierra el 4 de diciembre de 2026. Hasta entonces puedes seguir usándola o cancelar el cierre.',
    );
  });

  it('falls back to UTC when the time zone of the profile is unknown', () => {
    expect(
      closureNotice({
        accountStatus: 'scheduled',
        closesAt: '2026-12-05T02:00:00Z',
        timezone: 'No/Existe',
        lastClosure: null,
      }),
    ).toBe(
      'Tu cuenta se cierra el 5 de diciembre de 2026. Hasta entonces puedes seguir usándola o cancelar el cierre.',
    );
  });

  it('still announces a scheduled closure that arrives without its date', () => {
    expect(
      closureNotice({
        accountStatus: 'scheduled',
        closesAt: null,
        timezone: 'UTC',
        lastClosure: null,
      }),
    ).toBe(
      'El cierre de tu cuenta está programado. Hasta entonces puedes seguir usándola o cancelar el cierre.',
    );
  });

  it('says the account is closing while the closure runs', () => {
    expect(closureNotice({ ...active, accountStatus: 'closing', lastClosure: null })).toBe(
      'Tu cuenta se está cerrando. Mientras tanto no puedes crear tareas nuevas.',
    );
  });

  it('confirms a closure the user cancelled during the grace period', () => {
    expect(
      closureNotice({
        ...active,
        lastClosure: { ...rejected, outcome: 'cancelled', reason: null },
      }),
    ).toBe('Cancelaste el cierre de tu cuenta. Tus datos siguen igual.');
  });

  it('explains a closure rejected because the account is the last SuperAdmin', () => {
    expect(
      closureNotice({
        ...active,
        lastClosure: { ...rejected, reason: 'Auth.LastSuperAdmin' },
      }),
    ).toBe('No se cerró tu cuenta: es la única cuenta con permiso para administrar usuarios.');
  });

  it('falls back to a generic line for any other rejection', () => {
    expect(
      closureNotice({
        ...active,
        lastClosure: { ...rejected, reason: 'Auth.Other' },
      }),
    ).toBe('No se cerró tu cuenta. Tus datos siguen igual.');
  });

  it('shows nothing for an active account without closures', () => {
    expect(closureNotice({ ...active, lastClosure: null })).toBeNull();
  });
});
