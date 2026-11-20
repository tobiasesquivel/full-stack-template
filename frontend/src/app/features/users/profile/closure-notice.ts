import type { UserResponse } from '../api/user-response';

function closingDate(closesAt: string, timezone: string): string {
  const date = new Date(closesAt);
  try {
    return new Intl.DateTimeFormat('es', { dateStyle: 'long', timeZone: timezone }).format(date);
  } catch {
    return new Intl.DateTimeFormat('es', { dateStyle: 'long', timeZone: 'UTC' }).format(date);
  }
}

export function closureNotice(
  user: Pick<UserResponse, 'accountStatus' | 'closesAt' | 'timezone' | 'lastClosure'>,
): string | null {
  if (user.accountStatus === 'scheduled') {
    return user.closesAt
      ? `Tu cuenta se cierra el ${closingDate(user.closesAt, user.timezone)}. Hasta entonces puedes seguir usándola o cancelar el cierre.`
      : 'El cierre de tu cuenta está programado. Hasta entonces puedes seguir usándola o cancelar el cierre.';
  }
  if (user.accountStatus === 'closing') {
    return 'Tu cuenta se está cerrando. Mientras tanto no puedes crear tareas nuevas.';
  }
  if (user.lastClosure?.outcome === 'cancelled') {
    return 'Cancelaste el cierre de tu cuenta. Tus datos siguen igual.';
  }
  if (user.lastClosure?.outcome === 'rejected') {
    return user.lastClosure.reason === 'Auth.LastSuperAdmin'
      ? 'No se cerró tu cuenta: es la única cuenta con permiso para administrar usuarios.'
      : 'No se cerró tu cuenta. Tus datos siguen igual.';
  }
  return null;
}
