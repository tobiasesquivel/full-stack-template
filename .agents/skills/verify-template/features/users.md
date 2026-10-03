# Perfil

El usuario consulta su identidad y cambia nombre visible y zona horaria. Identity conserva el nombre de login y el correo.

## Sub-features

- `users-me` presenta la identidad autenticada.
- `users-update` guarda nombre visible y zona IANA válida.
- `users-invalid-zone` rechaza una zona inválida sin persistirla.
- `users-authorization` impide leer o borrar el perfil ajeno sin permiso.

## How to get to it (user POV)

- Abre `/profile` con una sesión activa.
- Pulsa el enlace `Mi perfil` de la navegación principal.
- Un cliente HTTP usa `GET` y `PUT /api/v1/users/me`, y `GET` y `DELETE /api/v1/users/{authId}`.

## Driving it with browser and verify-template

Preconditions:

- `doctor` pasó y el usuario inició sesión mediante registro o login real.

Pulsa `Mi perfil`. Exige el heading `Mi perfil` y una respuesta `GET /api/v1/users/me` 200. Rellena `#profile-display` con `Perfil verificado` y `#profile-timezone` con `America/Argentina/Buenos_Aires`. Pulsa `Guardar`. Exige PUT 200 y el status `Perfil actualizado.`.

Recarga `/profile`. Espera la restauración de sesión. Exige que ambos inputs conserven los valores. Captura antes, después y la segunda lectura. Cambia la zona a `No/Existe`. Pulsa `Guardar`. Exige 400 y un mensaje de error. Recarga y comprueba que siga la zona válida anterior.

Ejecuta `node .agents/skills/verify-template/scripts/verify.mjs api --run-id repro-template`. El recorrido verifica perfil, zona inválida, lectura ajena 403 y borrado de usuarios de prueba con revocación de sesión.

## Gotchas

- `Guardar` también aparece en Tasks. Abre `/profile` antes de usar ese selector.
- Un mensaje de guardado no prueba persistencia. La recarga debe recuperar ambos campos.
- Los usuarios de prueba pertenecen a PostgreSQL aislado. El cleanup no borra cuentas de la base del desarrollador.
- Un fallo previo deja el perfil `NOT_RUN`, no verificado por el contrato escrito.
