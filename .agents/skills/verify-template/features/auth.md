# Autenticación

El usuario crea una cuenta, entra, restaura su sesión al recargar y sale. La sesión vive en el servidor. El navegador solo guarda la cookie `__Host-session`, HttpOnly. Ninguna respuesta lleva un token.

## Sub-features

- `auth-register` crea identidad, perfil y sesión.
- `auth-login` acepta credenciales válidas y rechaza credenciales incorrectas.
- `auth-restore` restaura la sesión tras recargar con `GET /api/v1/users/me`, sin leer tokens del almacenamiento JavaScript.
- `auth-logout` borra la sesión en el servidor y regresa a login. La misma cookie da 401 después.

## How to get to it (user POV)

- Abre `/register` o el enlace `Crear cuenta`.
- Abre `/login` o el enlace `Entrar`.
- Con sesión activa, pulsa `Salir` en la navegación principal.
- Un cliente HTTP usa `/api/v1/auth/csrf`, `/register`, `/login` y `/logout` bajo `/api/v1/auth`, y `GET /api/v1/users/me` para saber quién es.

## Driving it with browser and verify-template

Preconditions:

- El helper publicó la URL de Angular en `run.json` y `doctor` pasó.
- Usa un usuario nuevo `verify` seguido de un sufijo único de esta ejecución. Usa un correo del dominio reservado `example.test`.

En el navegador real, abre `/register`. Rellena `#register-user`, `#register-email`, `#register-display` y `#register-password`. Usa una contraseña sintética larga con mayúscula, minúscula, número y símbolo. Pulsa el botón de submit del formulario, no el enlace de navegación con el mismo nombre. Exige navegación a `/tasks`, el heading `Mis tareas` y una respuesta 201 de `POST /api/v1/auth/register`.

Guarda una captura del formulario antes de enviar, sin mostrar la contraseña. Guarda el estado posterior y los metadatos de la request, sin body de credenciales ni cookies. Recarga la página. Exige `GET /api/v1/users/me` 200 y permanencia en `/tasks`.

Pulsa `Salir`. Exige `POST /api/v1/auth/logout` 204 y la ruta `/login`. Rellena `#login-user` y `#login-password`, y pulsa el submit `Entrar`. Exige regreso a `/tasks`. Prueba una contraseña incorrecta en una sesión nueva. Exige 401 y el mensaje `Usuario o contraseña incorrectos.`. No atribuyas un fallo de CSRF a credenciales incorrectas.

Ejecuta `node .agents/skills/verify-template/scripts/verify.mjs api --run-id repro-template` para el recorrido HTTP reproducible. El smoke prueba CSRF, registro duplicado, los atributos de `__Host-session`, que `XSRF-TOKEN` es la única cookie legible, que ningún body lleva un token, antiforgery en una escritura, la restauración con la cookie sola y el 401 de una sesión cerrada. El script no imprime credenciales ni cookies.

## Gotchas

- El bootstrap primero solicita CSRF y después `GET /users/me`. Un 401 sin sesión es esperado, pero un registro fallido no lo es.
- `Crear cuenta` y `Entrar` aparecen como enlaces y botones. Limita el selector al formulario o al rol button.
- Toda escritura de la API exige antiforgery, también registro y login. Envía el token de la misma cookie jar y el header `X-XSRF-TOKEN`. Abrir o cerrar sesión renueva `XSRF-TOKEN`.
- Las pestañas comparten la cookie y la sesión. `Salir` en una pestaña lleva a las otras a `/login` por `BroadcastChannel`.
- `__Host-session` es `Secure`. El navegador la acepta en `http://127.0.0.1` y `http://localhost`; en otro host por HTTP la descarta.
- Si build, tests o launch fallan, el recorrido de autenticación queda `NOT_RUN`.
