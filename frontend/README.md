# Frontend

Angular 22.2.1. La app consume la API del mismo origen bajo `/api/v1`
(registro, login, perfil y tareas). Los textos de la interfaz están en español.
## Desarrollo

Sin suite vigente 2026-10-05: no existe `*.spec.ts` (`app.spec` eliminado incidental) y `npm test` (`ng test`) no tiene runner configurado. La prueba real es el navegador contra la API (`artifacts/compose-final/browser-result.json`, `browser-network.json`, `tasks.png`): register, create, dos reloads sin perder login, checkbox persiste, perfil persiste, logout y login.

```bash
npm ci
npm start
```

`npm start` usa `proxy.conf.json` y reenvía `/api/v1` a
`http://localhost:8080`, que es donde se espera el backend local.
Si tu backend escucha en otro puerto, ajusta el `target` de ese archivo
(la app siempre llama a rutas relativas `/api/v1/...`).

## Sesión y CSRF

- El access token vive solo en memoria. No se guarda en
  `localStorage` ni `sessionStorage`.
- La sesión persistente es la cookie HttpOnly de refresh. Al recargar,
  la app pide `GET /api/v1/auth/csrf`, intenta `POST /api/v1/auth/refresh`
  y luego carga `GET /api/v1/users/me`.
- Angular envía el header `X-XSRF-TOKEN` a partir de la cookie legible
  `XSRF-TOKEN` en las mutaciones. Registro, login, refresh y logout
  piden primero el bootstrap CSRF.
- Ante un 401 de la API (excepto endpoints de auth), el interceptor
  renueva la sesión una sola vez con singleflight y repite la petición
  una sola vez. Sin bucles ni reintentos adicionales. Si la renovación
  falla, limpia la memoria y vuelve al login.
- `POST /api/v1/auth/logout` revoca la cookie y limpia la memoria.
  Si falla (por ejemplo sin red), la sesión local se conserva y la
  interfaz ofrece reintentarlo.

## Rutas

| Ruta        | Acceso | Contenido                              |
| ----------- | ------ | -------------------------------------- |
| `/login`    | libre  | Entrar con usuario y contraseña        |
| `/register` | libre  | Crear cuenta                           |
| `/tasks`    | sesión | Lista paginada y CRUD de tareas        |
| `/profile`  | sesión | Datos y edición de nombre visible y zona horaria |

Los guards esperan a que el arranque termine antes de decidir:
sin sesión van al login, con sesión el login/registro redirigen a tareas.

## Tareas

- Crear con título (1 a 200 caracteres), listar paginado de 10 por página,
  editar título, marcar hecha/pendiente con PUT de estado final
  (`{ title, isCompleted }`) y eliminar con confirmación.
- Los errores 400, 401, 403, 404 y 409 y la pérdida de conexión muestran
  un mensaje en español con acción de reintento explícita
  (botón Reintentar o repetir la acción). Nada reintenta solo.

## Docker

```bash
docker build -t frontend ./frontend
```

La imagen compila con Node 26, sirve la SPA con nginx en el puerto
`8080` y redirige `/api/v1/` al backend con `proxy_pass http://api:8080`
sin reescribir la ruta. Las cookies (`refresh` HttpOnly y `XSRF-TOKEN`)
pasan intactas. Detalle de integración acordado con Operations:
servicio backend `api` en su puerto interno `8080`, sin `proxy_cookie_path`
ni tratamiento CSRF especial en nginx.
