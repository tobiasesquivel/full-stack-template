import type { UserResponse } from '../api/user-response';

export function closureNotice(
  user: Pick<UserResponse, 'accountStatus' | 'lastClosure'>,
): string | null {
  if (user.accountStatus === 'closing') {
    return 'Tu cuenta se está cerrando. Mientras tanto no puedes crear tareas nuevas.';
  }
  if (user.lastClosure?.outcome === 'rejected') {
    return user.lastClosure.reason === 'Auth.LastSuperAdmin'
      ? 'No se cerró tu cuenta: es la única cuenta con permiso para administrar usuarios.'
      : 'No se cerró tu cuenta. Tus datos siguen igual.';
  }
  return null;
}
