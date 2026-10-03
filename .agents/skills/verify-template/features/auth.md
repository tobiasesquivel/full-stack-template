# Autenticación

El usuario crea una cuenta, entra, restaura su sesión al recargar y sale. El navegador conserva el refresh token solo en una cookie HttpOnly. El access token permanece en memoria.

## Sub-features

- `auth-register` crea identidad, perfil y sesión.
- `auth-login` acepta credenciales válidas y rechaza credenciales incorrectas.
- `auth-restore` restaura la sesión tras recargar sin leer tokens del almacenamiento JavaScript.
- `auth-refresh` rota el refresh token y rechaza su replay.
- `auth-logout` revoca la sesión y regresa a login.

## How to get to it (user POV)

- Abre `/register` o el enlace `Crear cuenta`.
- Abre `/login` o el enlace `Entrar`.
- Con sesión activa, pulsa `Salir` en la navegación principal.
- Un cliente HTTP usa `/api/v1/auth/csrf`, `/register`, `/login`, `/refresh` y `/logout` bajo `/api/v1/auth`.

## Driving it with browser and verify-template

Preconditions:

- El helper publicó la URL de Angular en `run.json` y `doctor` pasó.
- Usa un usuario nuevo `verify` seguido de un sufijo único de esta ejecución. Usa un correo del dominio reservado `example.test`.

En el navegador real, abre `/register`. Rellena `#register-user`, `#register-email`, `#register-display` y `#register-password`. Usa una contraseña sintética larga con mayúscula, minúscula, número y símbolo. Pulsa el botón de submit del formulario, no el enlace de navegación con el mismo nombre. Exige navegación a `/tasks`, el heading `Mis tareas` y una respuesta 201 de `POST /api/v1/auth/register`.

Guarda una captura del formulario antes de enviar, sin mostrar la contraseña. Guarda el estado posterior y los metadatos de la request, sin body de credenciales ni cookies. Recarga la página. Exige `POST /api/v1/auth/refresh` 200, `GET /api/v1/users/me` 200 y permanencia en `/tasks`.

Pulsa `Salir`. Exige `POST /api/v1/auth/logout` 204 y la ruta `/login`. Rellena `#login-user` y `#login-password`, y pulsa el submit `Entrar`. Exige regreso a `/tasks`. Prueba una contraseña incorrecta en una sesión nueva. Exige 401 y el mensaje `Usuario o contraseña incorrectos.`. No atribuyas un fallo de CSRF a credenciales incorrectas.

Ejecuta `node .agents/skills/verify-template/scripts/verify.mjs api --run-id repro-template` para el recorrido HTTP reproducible. El smoke prueba CSRF, registro duplicado, ausencia de permisos en JWT, rotación, replay y revocación del access token. El script no imprime credenciales ni tokens.

## Gotchas

- El bootstrap primero solicita CSRF y después refresh. Un refresh 401 sin sesión es esperado, pero un registro fallido no lo es.
- `Crear cuenta` y `Entrar` aparecen como enlaces y botones. Limita el selector al formulario o al rol button.
- Registro y login también exigen antiforgery. Envía el token de la misma cookie jar y el header `X-XSRF-TOKEN`.
- Varias pestañas pueden competir por un refresh. La política fail-closed puede exigir nuevo login. Guarda el resultado real y no lo reemplaces por un mock.
- Si build, tests o launch fallan, el recorrido de autenticación queda `NOT_RUN`.
