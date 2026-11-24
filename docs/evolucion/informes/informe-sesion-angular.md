# Informe: manejo de sesión en Angular (v20-v22)

**Alcance**: solo investigación, para el paso 0.14. Leí fuentes web primarias, las referencias de la skill oficial `angular-developer`, los tipos de `@angular/router` y `@angular/common` 22.2.1 instalados, el frontend del repo (ya con las carpetas del paso 0.13) y la feature `Auth` del backend. No cambié código.

**Distinción de fuentes**: **[PRIMARIA]** = el equipo de Angular, IETF, OWASP, MDN o el código instalado. **[OPINIÓN]** = practicante o proveedor que interpreta a otros. **[INFERENCE]** = razonamiento mío sobre lo leído. **[REPO]** = observado en el código, con `archivo:línea`.

**Términos**: uso **sesión** como en [CONTEXT.md](../../../CONTEXT.md): un acceso autenticado que se renueva y se cierra. **Abrir**, **renovar** y **cerrar** siguen a las slices del backend: `OpenSession`, `RefreshSession` y `CloseSession`. **Restaurar** es recuperar la sesión al recargar la página.

---

## Conclusión

1. **El diseño de seguridad del repo es aceptable y profesional.** El access token vive en memoria y el refresh en una cookie HttpOnly. Es el patrón que el RFC 10017 llama *token-mediating backend*: el segundo en seguridad. El más seguro es el BFF (todo por cookie, ningún token en el navegador). Pasar a BFF cambia el contrato HTTP: es **⏸ DECISIÓN DE TOBI**. [PRIMARIA + INFERENCE] **Tobi eligió BFF el 2026-10-17**: lo aplica el paso 0.26 del [plan](../plan.md).
2. **Para una app de este tamaño, la práctica recomendada es un servicio `@Service()` con signals**, no NgRx SignalStore. El estado va en un `signal` privado; lo público son `computed` o `asReadonly()`. El repo ya hace eso a medias. [PRIMARIA]
3. **El estado de la sesión debería ser uno solo, con tres valores**: `restoring`, `open` y `closed`. Hoy son tres piezas sueltas: `currentUser`, `initialized` y una `Promise` armada a mano. Con un solo estado no hay combinaciones imposibles. [INFERENCE sobre PRIMARIA]
4. **El interceptor tiene un error de comportamiento.** Si la petición reintentada falla por cualquier motivo (404, 500, 400), cierra la sesión y manda a `/login`. Solo debería cerrarla si falla la renovación. [REPO]
5. **Dos pestañas que recargan a la vez cierran la sesión en las dos.** El backend detecta el reuso del refresh token y revoca la familia: lo prueba `RefreshSessionTests.cs:17`. La Web Locks API lo resuelve solo en el frontend, sin cambiar el contrato. [REPO + PRIMARIA]
6. **Faltan tres prácticas comunes**: volver a la página pedida después del login (`returnUrl`), marcar las peticiones de `/auth` con `HttpContextToken` en lugar de comparar la URL, y avisar a las otras pestañas cuando la sesión se cierra (`BroadcastChannel`). [PRIMARIA + OPINIÓN]
7. **Lo que ya está bien y no cambia**: guards funcionales que devuelven `UrlTree`, una sola renovación por pestaña (`refreshPromise`), XSRF con `withXsrfConfiguration`, `provideAppInitializer()`, nada en `localStorage`, y el logout que no borra la sesión local si el servidor no la cerró. [REPO]

---

## 1. Cómo se modela el estado de la sesión

### 1.1 Qué dicen las fuentes

| Fuente | Tipo | Qué dice |
|---|---|---|
| `signals-overview.md` de la skill oficial | PRIMARIA | *"When exposing state from a service, it is a best practice to expose a readonly version to prevent external mutation."* Patrón: `private readonly _count = signal(0); readonly count = this._count.asReadonly();` |
| `creating-services.md` de la skill oficial | PRIMARIA | Para estado reactivo: el dato en un `signal()` privado y lo público con `.asReadonly()`. Lo derivado de varios servicios, con `computed()`. |
| `linked-signal.md` y `effects.md` | PRIMARIA | `computed` para lo derivado; `linkedSignal` si además hay que escribirlo. *"DO NOT use effects to propagate state."* |
| `resource.md` y `http-client.md` | PRIMARIA | `resource()` y `httpResource()` sirven para **lecturas** que dependen de signals. Las mutaciones (`POST`, `PUT`) van con `HttpClient`. |
| `naming-conventions.md` | PRIMARIA | *Intent over Role*: el nombre dice el propósito (`auth-data.ts` / `AuthData`). Los clientes HTTP usan `-api` o `-client`. |
| [abp.io, "Angular 22 State Management"](https://abp.io/community/articles/angular-22-state-management-signals-signalstore-or-ngrx-yq8zg0nw) y otros artículos | OPINIÓN | Regla común: estado local → `signal`; estado compartido de una feature → servicio con signals o SignalStore; estado global con eventos y devtools → NgRx Store. "Empezá simple y sumá estructura cuando haga falta." |

### 1.2 Qué conviene aquí

- **Servicio con signals, no SignalStore.** La sesión tiene un solo dueño (`Session`) y pocas transiciones. NgRx sería una librería nueva: si Tobi la quiere, es ⏸ DECISIÓN DE TOBI. [INFERENCE]
- **`resource()` no encaja en el núcleo de la sesión.** La sesión cambia por acciones (abrir, cerrar, renovar), no por un signal de entrada. `resource()` sí encaja en `Profile`, que lee `/users/me`. Eso es de la feature `users` y queda fuera del paso. [INFERENCE sobre PRIMARIA]
- **Un estado con tres valores.** `restoring` (al arrancar), `open` (con el usuario) y `closed`. Es una *unión discriminada* de TypeScript: el compilador impide tener un usuario con la sesión cerrada. [INFERENCE]
- **El nombre `Session` queda.** Coincide con CONTEXT.md, con las slices del backend y con `conventions.md`. Las acciones toman los verbos del backend: `open()`, `refresh()`, `close()`, más `restore()` y `register()`. [INFERENCE]
- **El HTTP sale de `Session` a un `AuthApi`.** Así queda igual que `UsersApi` y `TasksApi`: `api/` tiene el cliente y sus modelos; `session/` tiene el estado, los guards y el interceptor. Es la división del paso 0.13. [REPO + INFERENCE]

---

## 2. Restaurar la sesión al recargar y esperar en los guards

### 2.1 Qué dicen las fuentes

| Fuente | Tipo | Qué dice |
|---|---|---|
| [provideAppInitializer](https://angular.dev/api/core/provideAppInitializer) | PRIMARIA | *"If the function returns a Promise or an Observable, initialization does not complete until the Promise is resolved."* La función corre en contexto de inyección. `APP_INITIALIZER` *"is now deprecated"*. |
| [Route guards](https://angular.dev/guide/routing/route-guards) | PRIMARIA | `canActivate` es *"most commonly used for authentication"*. `canActivateChild` *"runs for all children"*. Para redirigir: *"return a URLTree or RedirectCommand. Do not return false and then programmatically navigate"*. Un guard no debe depender de lo que hizo otro guard del mismo array: el trabajo compartido va en un servicio. *"Never rely on client-side guards as the sole source of access control."* |
| `@angular/router` 22.2.1, `Route.canLoad` | PRIMARIA | `canLoad` está `@deprecated Use canMatch instead`. `canMatch` es el que evita descargar un chunk lazy. |
| `RedirectCommand` en los mismos tipos | PRIMARIA | `new RedirectCommand(urlTree, navigationBehaviorOptions)`: redirige con opciones de navegación (por ejemplo `replaceUrl`). |

### 2.2 Qué conviene aquí

- **Arranque sin bloquear, como hoy.** El paso 0.9 eligió que `restore()` no bloquee: el header aparece enseguida y `<main>` muestra "Cargando…". Bloquear el arranque (devolver la `Promise`) también es válido y lo muestra la doc. Pero cambia lo que ve el usuario y deja la pantalla en blanco si el backend tarda. No lo propongo. [REPO + INFERENCE]
- **Los guards esperan al estado, no a una `Promise` aparte.** Hoy `Session` arma una `Promise` con `readyResolve` a mano (`session.ts:23-30`). Con el estado único, el guard espera a que `status()` deje de ser `restoring` con `toObservable(...).pipe(first(...))`. Hay una sola fuente de verdad. [INFERENCE]
- **`canActivateChild` queda.** `canMatch` evitaría descargar los chunks de tareas y perfil sin sesión. Pero esos chunks no tienen secretos, el servidor valida cada petición, y `canMatch` sobre rutas con `path: ''` se evalúa en **toda** navegación: un `guestGuard` que devuelve `UrlTree` ahí arma un bucle de redirecciones. Además `canActivateChild` recibe `state.url`, que hace falta para el `returnUrl`. [PRIMARIA + INFERENCE]
- **`returnUrl`.** El `authGuard` devuelve `router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } })`. Después de abrir la sesión, `Login` navega a ese valor **solo si es una ruta interna** (empieza con `/` y no con `//`). Si no, va a `/tasks`. Así no hay redirección abierta a otro sitio. [INFERENCE; práctica común]

---

## 3. Renovar el token en el interceptor

### 3.1 Qué dicen las fuentes

| Fuente | Tipo | Qué dice |
|---|---|---|
| [Interceptors](https://angular.dev/guide/http/interceptors) y `http-client.md` | PRIMARIA | Interceptores funcionales con `withInterceptors`. `HttpContextToken` para *"per-request metadata that interceptors need but the backend should not receive"*: `new HttpContextToken<boolean>(() => false)`, se lee con `req.context.get(TOKEN)`. Las peticiones son inmutables: se clonan. |
| [Security](https://angular.dev/best-practices/security) | PRIMARIA | El interceptor XSRF lee la cookie `XSRF-TOKEN` y manda `X-XSRF-TOKEN` en *"all mutating requests (such as POST) to relative and same origin URLs"*. El backend pone la cookie y valida el header. |
| `http-client.md` | PRIMARIA | `HttpClient` usa `fetch` por defecto. `withCredentials: true` solo hace falta entre orígenes distintos. |
| [OpenReplay, "Fix token refresh race with Web Locks"](https://blog.openreplay.com/fix-token-refresh-race-web-locks-api/) (2026-09-09) | OPINIÓN | Una renovación compartida; reintento **una vez**; un 401 del propio endpoint de renovación cierra la sesión y no reintenta. |

### 3.2 Comportamiento recomendado

1. **Solo `/api/` lleva el token.** Hoy el interceptor agrega `Authorization` a cualquier URL (`auth-interceptor.ts:11-12`). Un `fetch` a otro dominio filtraría el token. [REPO]
2. **Las peticiones de `/auth` se marcan con un `HttpContextToken`** (`SKIP_SESSION`), no con `request.url.startsWith('/api/v1/auth/')` (`auth-interceptor.ts:19`). El marcador viaja con la petición y no depende del texto de la URL. Esas peticiones no llevan `Authorization` ni se renuevan. [PRIMARIA]
3. **Una sola renovación a la vez.** Ya está: `refreshPromise` en `session.ts:22` y `session.ts:78-83`. Varias respuestas 401 esperan la misma `Promise`. [REPO]
4. **Reintento una vez, y el error del reintento sale tal cual.** Hoy el `catchError` de `auth-interceptor.ts:29-33` envuelve también el reintento: un 404 o un 500 del reintento cierra la sesión, y devuelve el 401 original en lugar del error real. El `catchError` tiene que ir **antes** del `switchMap`, solo sobre la renovación. [REPO]
5. **Si la renovación falla, la sesión termina en un solo lugar.** `Session.end()` borra el estado y navega a `/login?returnUrl=...`. Hoy lo hacen el interceptor (`auth-interceptor.ts:30-31`) y `doRefresh` (`session.ts:98`) por separado. [REPO + INFERENCE]
6. **XSRF y `withCredentials`.** La configuración actual está bien (`app.config.ts:14`): los nombres coinciden con `SessionHttpPolicy.cs:8` y `AuthFeature.cs:116`. La app y la API comparten origen (proxy), así que `fetch` manda las cookies sin `withCredentials`. No se agrega. [REPO + PRIMARIA]
7. **Renovar al recibir 401, sin timer.** El token dura 15 minutos (`appsettings.json`) y el servidor valida la familia en cada petición (`SessionValidator.cs`). Un timer que renueva antes del vencimiento suma estado y no mejora la seguridad. Queda reactivo. [INFERENCE]

**Un riesgo escondido que el punto 2 corrige** [INFERENCE, a confirmar en el navegador]. ASP.NET ata el token antiforgery al usuario de la petición. Hoy `/auth/csrf` y `/auth/logout` viajan con `Authorization`, y `/auth/refresh` también, con un token que puede estar vencido. Si el token XSRF se emitió con un usuario y la renovación llega como anónima, el backend responde 400 y la sesión se cierra. Si ninguna petición de `/auth` lleva `Authorization`, todas son anónimas y el token XSRF siempre coincide. `CloseSession` usa solo la cookie, así que no necesita el JWT.

---

## 4. Seguridad: token en memoria + cookie HttpOnly frente a BFF

### 4.1 Qué dicen las fuentes

| Fuente | Tipo | Qué dice |
|---|---|---|
| [RFC 10017 / BCP 212, *OAuth 2.0 for Browser-Based Applications*](https://www.rfc-editor.org/rfc/rfc10017.html) (agosto 2026) | PRIMARIA | Tres arquitecturas *"in decreasing order of security"*: BFF, token-mediating backend y cliente OAuth en el navegador. El BFF es *"strongly recommended for business applications, sensitive applications"* y datos personales. En el token-mediating backend *"the access token is directly exposed to the application"*, y no hace falta guardar tokens de forma persistente. Guardar en memoria tiene *"the downside of not being able to persist tokens between page loads"*. Las cookies del BFF: `Secure`, `HttpOnly`, `SameSite=Strict`, prefijo `__Host-`, y una defensa CSRF obligatoria. |
| [OWASP Session Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html) | PRIMARIA | No guardar tokens en Web Storage: *"a single XSS vulnerability discloses every token"*. El logout debe invalidar la sesión *"at least on server side"*. Timeout por inactividad y absoluto, del lado del servidor. Prefijo `__Host-` para la cookie de sesión. `Path` lo más restrictivo posible. |
| [Duende BFF](https://docs.duendesoftware.com/bff/) | OPINIÓN (proveedor) | Para una SPA que llama APIs autenticadas, BFF es la arquitectura recomendada. La librería pide licencia en producción. |

### 4.2 Evaluación

- **El repo no usa OAuth**: el backend emite sus propios JWT. El RFC aplica por analogía. La cookie HttpOnly del refresh hace el papel de la sesión del backend; el JWT en memoria es el access token expuesto. Es el segundo nivel del RFC. [INFERENCE]
- **Cumple lo central de OWASP**: nada en Web Storage, logout que revoca en el servidor (`CloseSession` revoca la familia), rotación con detección de reuso, timeout absoluto (`TokenService.cs:197-199` no deja pasar el vencimiento original). [REPO]
- **El riesgo que queda es XSS.** Un script inyectado puede leer el JWT de memoria y usarlo hasta 15 minutos, o pedir uno nuevo, porque la cookie viaja sola. El BFF elimina lo primero. [PRIMARIA + INFERENCE]
- **Qué cambiaría un BFF aquí.** La app y la API comparten origen. Un BFF se reduce a autenticación por cookie de ASP.NET Core: sin JWT en el navegador, sin interceptor de `Authorization`, sin renovación en el frontend. No hace falta Duende. Cambia el contrato HTTP de `/auth` y la autenticación del backend. **⏸ DECISIÓN DE TOBI.** No es urgente: el diseño actual es una opción profesional aceptada. [INFERENCE]
- **Detalles de la cookie** (backend): `template_refresh` no tiene prefijo. `__Host-` exige `Path=/`, y hoy es `Path=/api/v1/auth` (`SessionHttpPolicy.cs:72-78`). `__Secure-` sí es compatible. **⏸ DECISIÓN DE TOBI.**
- **Inactividad**: el backend tiene timeout absoluto pero no por inactividad. Un timer en el frontend solo mejora la experiencia; OWASP lo pide en el servidor. Es un cambio de backend: **⏸ DECISIÓN DE TOBI**.

---

## 5. Varias pestañas

### 5.1 Qué dicen las fuentes

| Fuente | Tipo | Qué dice |
|---|---|---|
| [MDN, Web Locks API](https://developer.mozilla.org/en-US/docs/Web/API/Web_Locks_API) | PRIMARIA | `navigator.locks.request(nombre, callback)` da un lock por nombre, compartido entre pestañas y workers del mismo origen. Se libera solo cuando termina el callback. Baseline desde marzo 2022. Solo en contexto seguro (HTTPS o `localhost`). |
| [MDN, BroadcastChannel](https://developer.mozilla.org/en-US/docs/Web/API/BroadcastChannel) | PRIMARIA | Canal entre pestañas del mismo origen. El que envía no recibe su propio mensaje. Baseline desde marzo 2022. |
| [Auth0, refresh token rotation](https://auth0.com/docs/secure/tokens/refresh-tokens/configure-refresh-token-rotation) | PRIMARIA (proveedor) | Ofrece un *Rotation Overlap Period*: un margen para que un token recién rotado no dispare la detección de reuso. |
| [OpenReplay](https://blog.openreplay.com/fix-token-refresh-race-web-locks-api/) (2026-09-09) | OPINIÓN | Con rotación, dos pestañas que renuevan a la vez cierran la sesión en todas. La solución es envolver la renovación en `navigator.locks.request`. Un flag en `localStorage` no sirve como lock. |
| OWASP Session Management | PRIMARIA | Las cookies se comparten entre pestañas: no se puede separar la sesión por pestaña si viaja en cookie. |

### 5.2 Qué pasa hoy en el repo

- **Recarga simultánea.** Dos pestañas mandan la misma cookie a `/refresh`. Una rota; la otra presenta un token ya revocado y `TokenService.cs:182-188` revoca toda la familia. Las dos quedan sin sesión. El test `Refresh_ConcurrentSameToken_OnlyOneRotates_And_FamilyRevoked_OtherSessionSurvives` (`RefreshSessionTests.cs:17`) confirma que es el comportamiento buscado del backend. [REPO]
- **Logout en otra pestaña.** La pestaña B no se entera hasta su próxima petición: el 401 dispara una renovación que falla (la cookie ya no está) y recién ahí va a `/login`. [REPO + INFERENCE]
- **Login con otro usuario en otra pestaña.** La cookie nueva pisa la anterior. Cuando B renueva, recibe un token del usuario nuevo, pero su header sigue mostrando al anterior: `refresh` no vuelve a leer `/users/me`. [INFERENCE]

### 5.3 Qué conviene

- **Web Locks alrededor de la renovación.** Dentro de una pestaña, `refreshPromise` junta las 401. Entre pestañas, el lock las pone en fila: la segunda renueva **después**, con la cookie nueva que dejó la primera, y la rotación no ve reuso. Es solo frontend. [PRIMARIA + INFERENCE]
- **`BroadcastChannel('session')` con dos mensajes**: `closed` (las otras pestañas terminan la sesión local y van a `/login`) y `opened` (las otras restauran, así toman al usuario nuevo). No es una exigencia de Angular ni de OWASP. Es práctica común y la API es estándar. [OPINIÓN + PRIMARIA]
- **Un margen de rotación en el backend** (como Auth0) cubriría también otros navegadores o pestañas sin Web Locks. Cambia el backend: **⏸ DECISIÓN DE TOBI**, y no hace falta si se aplica el lock.

---

## 6. Pruebas

La práctica oficial es Vitest con `TestBed`, `provideHttpClient()` y `provideHttpClientTesting()`; `HttpTestingController` permite comprobar que tres 401 producen un solo `POST /refresh`. El repo no tiene suite de frontend: crearla es otro paso. Aquí la prueba es el navegador con la skill `verify-template` y `playwright-cli` (sección 10). [PRIMARIA + REPO]

---

## 7. El repo hoy frente a la práctica recomendada

Las rutas son las del paso 0.13 (`features/auth/session/`). Las líneas no cambiaron con el movimiento.

| Tema | Hoy | Recomendado | Estado |
|---|---|---|---|
| Estado privado y lectura pública | `currentUserSignal` privado + `asReadonly()` (`session.ts:16-19`) | Igual | Cumple |
| Token en memoria | Campo privado `accessToken` (`session.ts:21`) | Igual; no es signal porque nadie lo pinta | Cumple |
| Un estado, sin combinaciones imposibles | Tres piezas: `currentUser`, `initialized` y `readyPromise` con `readyResolve` a mano (`session.ts:16-30`) | Un `signal` con `restoring` / `open` / `closed` | Cambiar |
| Derivados | `isAuthenticated()` es un método (`session.ts:36-38`) | `computed` | Cambiar |
| Escritura desde afuera | `setUser()` público (`session.ts:40-42`), lo usa `profile.ts:39` y `profile.ts:57` | Un método con nombre de acción (`replaceUser`) | Cambiar nombre |
| HTTP dentro de `Session` | `session.ts:57`, `:65`, `:73`, `:93`, `:116` | `AuthApi` en `auth/api/`, como `UsersApi` | Cambiar |
| Modelo del login | Objeto en línea `{ userName, password }` (`session.ts:65`) | `OpenSessionRequest`, como el backend | Cambiar |
| Imports sin uso | `UserResponse` en `register-request.ts:1` y `token-response.ts:1` | Sin import | Cambiar |
| Métodos internos públicos | `refreshNow`, `clearSession`, `ensureCsrf` son públicos (`session.ts:78`, `:85`, `:115`) | Públicos solo los que usa otro archivo | Cambiar |
| Navegación | `Session` navega al abrir y al cerrar (`session.ts:59`, `:68`, `:75`) | El componente navega (con `returnUrl`); `Session` navega solo cuando la sesión termina sola | Cambiar |
| Logout si falla el servidor | No borra la sesión local y la UI ofrece reintentar (`session.ts:71-76`, `app.ts:16-26`) | Igual: borrar solo lo local engaña en una computadora compartida, porque recargar restaura | Cumple |
| Arranque | `provideAppInitializer()` sin bloquear (`app.config.ts:16`) | Igual | Cumple |
| Guards | Funcionales, `UrlTree`, esperan `ready()` (`auth-guard.ts:8`, `:14`) | Esperan `status()`; `authGuard` agrega `returnUrl` | Cambiar |
| Dónde se aplican | `canActivateChild` en `app.routes.ts:8-9` | Igual (ver 2.2) | Cumple |
| Token solo a la API | A cualquier URL (`auth-interceptor.ts:11-12`) | Solo `/api/` | Cambiar |
| Marcar peticiones de `/auth` | Por prefijo de URL (`auth-interceptor.ts:19`) | `HttpContextToken` | Cambiar |
| Error del reintento | Cierra la sesión y devuelve el 401 original (`auth-interceptor.ts:29-33`) | Sale el error real; solo la renovación fallida cierra | Cambiar (error) |
| Fin de sesión | En dos lugares (`auth-interceptor.ts:30-31`, `session.ts:98`) y sin `returnUrl` | `Session.end()` | Cambiar |
| Varias pestañas | Sin coordinación | Web Locks + `BroadcastChannel` | Cambiar |
| XSRF | `withXsrfConfiguration` (`app.config.ts:14`) | Igual | Cumple |
| Storage del navegador | No se usa | Igual | Cumple |

---

## 8. Propuesta ordenada para el frontend

Regla 1 de AGENTS.md: un paso = un cambio observable. Propongo partir el paso 0.14 en tres. El orden importa: B y C se apoyan en la forma de A.

### Paso A (0.14) — Forma de la sesión (solo forma)

1. **`AuthApi`** en `features/auth/api/auth-api.ts`, con `@Service()`. Métodos: `issueCsrf()`, `register(request)`, `open(request)`, `refresh()`, `close()`. — *solo forma*
2. **Modelos a `features/auth/api/`**: `auth-response.ts`, `token-response.ts`, `register-request.ts` y uno nuevo, `open-session-request.ts` / `OpenSessionRequest`. Se borran los imports sin uso. — *solo forma*
3. **`Session`** (`features/auth/session/session.ts`) con esta forma pública:

   ```ts
   readonly status: Signal<'restoring' | 'open' | 'closed'>;
   readonly currentUser: Signal<UserResponse | null>;
   readonly isOpen: Signal<boolean>;
   accessToken(): string | null;
   restore(): Promise<void>;
   register(request: RegisterRequest): Promise<void>;
   open(request: OpenSessionRequest): Promise<void>;
   refresh(): Promise<void>;
   close(): Promise<void>;
   end(): void;
   replaceUser(user: UserResponse): void;
   ```

   Por dentro: un solo `signal` privado con la unión discriminada (el tipo queda sin `export`, FRONT010). `status`, `currentUser` e `isOpen` son `computed`. Desaparecen `initialized`, `ready()`, `readyResolve`, `setUser`, `clearSession`, `refreshNow` y `ensureCsrf` públicos. — *solo forma*
4. **`app.config.ts`**: `provideAppInitializer(() => inject(Session).restore())`, sin bloquear como hoy. **`app.html`**: `session.status() !== 'restoring'` e `session.isOpen()`. — *solo forma*
5. **Guards** (`auth-guard.ts`): esperan `toObservable(session.status).pipe(first((s) => s !== 'restoring'))`. Siguen como `CanActivateFn` y se aplican con `canActivateChild`. — *solo forma*
6. **`Login` y `Register`** navegan ellos después de `open()` / `register()`. — *solo forma* (mismo destino `/tasks`)
7. **`Profile`** usa `replaceUser()`. — *solo forma*
8. **Regla FRONT012** (sección 9).

### Paso B (0.15) — Interceptor y `returnUrl` (cambia comportamiento)

1. **`SKIP_SESSION`** en `features/auth/api/skip-session.ts`: `export const SKIP_SESSION = new HttpContextToken<boolean>(() => false);`. `AuthApi` marca con él cada petición. **El interceptor agrega `Authorization` solo a `/api/`** y nunca a las peticiones con `SKIP_SESSION`. Las de `/auth` dejan de llevar el JWT. — *cambia comportamiento*
2. **Reintento una vez; el error del reintento sale tal cual.** Forma:

   ```ts
   return from(session.refresh()).pipe(
     catchError(() => { session.end(); return throwError(() => error); }),
     switchMap(() => next(withToken(request, session.accessToken()))),
   );
   ```

   — *cambia comportamiento* (corrige el error del punto 4 de la conclusión)
3. **`Session.end()`** borra el estado y navega a `/login?returnUrl=<url actual>`. Es el único lugar que termina la sesión sin pedirlo el usuario. — *cambia comportamiento*
4. **`authGuard`** redirige a `/login?returnUrl=<state.url>`. **`Login`** vuelve a `returnUrl` si es una ruta interna (`/…`, no `//…`); si no, a `/tasks`. — *cambia comportamiento*

### Paso C (0.16) — Varias pestañas (cambia comportamiento)

1. **`refresh()` dentro de `navigator.locks.request('session-refresh', …)`**, sin perder `refreshPromise`. — *cambia comportamiento*
2. **`BroadcastChannel('session')`** en `Session`: al cerrar avisa `closed`; al abrir o registrar avisa `opened`. Al recibir `closed`, la pestaña llama a `end()` sin HTTP. Al recibir `opened`, llama a `restore()`. — *cambia comportamiento*

### Fuera del plan

- `Profile` con `httpResource()` para `/users/me`: es de la feature `users`. Va como pendiente en `progreso.md`.
- Distinguir "sin conexión" de "sin sesión" al restaurar (`session.ts:48` trata un `status 0` como sesión cerrada). Pendiente.
- Todo lo marcado ⏸ en la sección 11.

---

## 9. Regla ejecutable propuesta

**FRONT012: un `@Service()` no expone un `WritableSignal`.** Toda propiedad pública de una clase con `@Service()` o `@Injectable()` cuyo tipo sea `WritableSignal<T>` (lo que devuelven `signal()` y `linkedSignal()`) es un error. Lo público tiene que ser `Signal<T>`: `computed()` o `.asReadonly()`. Es la regla de `signals-overview.md` y `creating-services.md`. El gate ya usa el type checker de TypeScript, así que la regla mira el tipo y no el texto: un alias o un `as` no la esquiva.

- Fixture válida: `@Service()` con `private readonly state = signal(...)` y `readonly status = computed(...)`.
- Violación deliberada: `@Service()` con `readonly status = signal('closed')`.
- No aplica a componentes: sus signals son `protected` y solo los lee el template.

**Alternativa o complemento**: ampliar FRONT003 (hoy prohíbe `localStorage` y `sessionStorage` solo en componentes) a todo `frontend/src`. Hace ejecutable "las credenciales no se guardan en el storage del navegador" de `conventions.md`. Cambia un gate existente: necesita su línea en `decisions.tsv` (regla 9).

---

## 10. Verificación en el navegador

Con `verify-template` y `playwright-cli`, mirando la red. En modo desarrollo, `ng.getComponent(document.querySelector('app-root')).session` da acceso a `Session` desde la consola, y `page.route()` de Playwright permite responder 401 a propósito.

| Comportamiento | Cómo se observa | Paso |
|---|---|---|
| La recarga restaura la sesión | En `/tasks`, recargar: un `POST /auth/refresh`, un `GET /users/me`, sigue en `/tasks` con el nombre en el header | A |
| Sin sesión, una ruta protegida va al login | Contexto nuevo, abrir `/tasks`: termina en `/login?returnUrl=%2Ftasks` | B |
| El login vuelve a la página pedida | Desde ese estado, entrar: termina en `/tasks` | B |
| `returnUrl` externo se ignora | `/login?returnUrl=//example.com`: al entrar va a `/tasks` | B |
| Varias 401 producen una sola renovación | `page.route` responde 401 a las dos primeras peticiones de la API; disparar dos a la vez: un solo `POST /auth/refresh` y dos reintentos | B |
| La renovación fallida lleva al login | Borrar la cookie `template_refresh` y responder 401 a una petición: un `POST /auth/refresh` con 401 y la página termina en `/login?returnUrl=...` | B |
| Un error del reintento no cierra la sesión | 401 en la primera petición y 404 en el reintento: la sesión sigue y se ve el mensaje del 404 | B |
| Las peticiones de `/auth` no llevan `Authorization` | Revisar los headers de `/auth/csrf`, `/auth/refresh` y `/auth/logout` | B |
| La contraseña mala no dispara renovación | Login con contraseña mala: un `POST /auth/login` 401, ningún `/auth/refresh`, mensaje "Usuario o contraseña incorrectos." | B |
| Dos pestañas que recargan a la vez siguen abiertas | Dos páginas del mismo contexto, recargar las dos a la vez: dos `/auth/refresh` en fila, los dos 200. **Antes del cambio**: uno da 401 y las dos van a `/login` (rojo antes de verde) | C |
| El logout cierra las otras pestañas | Salir en la pestaña A: la B va a `/login` sin hacer nada | C |
| El login de otro usuario actualiza las otras pestañas | Entrar en A con otro usuario: el header de B muestra el usuario nuevo | C |
| Logout con el servidor caído | Cortar la API y pulsar Salir: la sesión sigue y aparece "Pulsa Salir de nuevo para reintentar." (sin cambio) | A |

Además: `ng build`, `node dune/check-frontend.mjs` y `node --test dune/check-frontend.test.mjs` con FRONT012 en rojo sobre la violación y en verde sobre la fixture.

---

## 11. Decisiones de Tobi

- **BFF: elegido por Tobi el 2026-10-17** (ver `docs/decisions.tsv` y el paso 0.26 del plan). Con BFF, los tres puntos que siguen cambian: la cookie de sesión lleva `__Host-` con `Path=/`, el timeout por inactividad lo da la sesión del servidor y el margen de rotación deja de existir porque no hay refresh token. Texto original:
  ⏸ **BFF (sesión solo por cookie, sin JWT en el navegador).** Es el nivel más seguro del RFC 10017. Cambia el contrato de `/auth`, la autenticación del backend, el interceptor y la renovación. No hace falta Duende: alcanza con la autenticación por cookie de ASP.NET Core, porque la app y la API comparten origen.
- ⏸ **Prefijo de la cookie de refresh** (`__Secure-`; `__Host-` obliga a `Path=/`). Backend.
- ⏸ **Timeout por inactividad en el servidor.** Hoy solo hay timeout absoluto. Backend.
- ⏸ **Margen de rotación del refresh token** (como el *Rotation Overlap Period* de Auth0). Backend. No hace falta si se aplica el paso C.
- ⏸ **NgRx SignalStore.** Librería nueva. No la recomiendo para este tamaño.
- **Partir el paso 0.14 en A, B y C** (sección 8). Es una decisión de proceso, no de herramienta, pero cambia el plan.
- **Nombres de las acciones** (`open`/`close` en lugar de `login`/`logout`). Siguen al backend y a CONTEXT.md. Tobi puede preferir los nombres de siempre.

---

## Verificación y límites

**Leído directamente**: [RFC 10017](https://www.rfc-editor.org/rfc/rfc10017.html) (secciones de arquitecturas, 6.1, 6.2 y 8, en partes); el [datatracker del borrador](https://datatracker.ietf.org/doc/draft-ietf-oauth-browser-based-apps/); [OWASP Session Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html); en angular.dev, [Route guards](https://angular.dev/guide/routing/route-guards), [Interceptors](https://angular.dev/guide/http/interceptors), [Security](https://angular.dev/best-practices/security), [provideAppInitializer](https://angular.dev/api/core/provideAppInitializer) y [CanMatchFn](https://angular.dev/api/router/CanMatchFn); MDN [Web Locks](https://developer.mozilla.org/en-US/docs/Web/API/Web_Locks_API) y [BroadcastChannel](https://developer.mozilla.org/en-US/docs/Web/API/BroadcastChannel); [Auth0, refresh token rotation](https://auth0.com/docs/secure/tokens/refresh-tokens/configure-refresh-token-rotation); [OpenReplay](https://blog.openreplay.com/fix-token-refresh-race-web-locks-api/); las referencias `http-client.md`, `route-guards.md`, `signals-overview.md`, `resource.md`, `linked-signal.md`, `effects.md`, `creating-services.md` y `naming-conventions.md` de la skill `angular-developer`; los tipos de `@angular/router` 22.2.1 (`canLoad` obsoleto, `RedirectCommand`) y de `@angular/common/http` (`HttpContextToken`).

**No verificado**:
- Las páginas se leyeron con una herramienta que resume. Las citas son textuales según esa herramienta, no copiadas a mano de la página.
- La sección 8.4 del RFC (memoria) se leyó resumida; no leí entera la sección de cookies del BFF.
- Que ASP.NET rechace un token XSRF emitido para otro usuario sale de cómo funciona antiforgery (ata el token a la identidad). El rojo del paso 0.15 lo confirmó: con el JWT vigente, `/auth/refresh` respondía 400 (`artifacts/verify-template/fase0-15-runtime-red/`).
- El caso "login con otro usuario en otra pestaña" sale de leer el código, no de una corrida.
- La OWASP JWT Cheat Sheet devolvió 404 en la URL que probé. No la usé.
- No encontré una guía de angular.dev dedicada a sesión o autenticación: la doc oficial cubre las piezas (guards, interceptores, XSRF, signals), no el diseño completo.
- Las cifras de soporte de navegadores (Baseline marzo 2022) son de MDN a la fecha de consulta.

## Fuentes

Consultadas el 2026-10-17.

- [RFC 10017 / BCP 212, OAuth 2.0 for Browser-Based Applications](https://www.rfc-editor.org/rfc/rfc10017.html), IETF, agosto 2026. [PRIMARIA]
- [draft-ietf-oauth-browser-based-apps](https://datatracker.ietf.org/doc/draft-ietf-oauth-browser-based-apps/), IETF datatracker, actualizado 2026-08-21. [PRIMARIA]
- [Session Management Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html), OWASP. [PRIMARIA]
- [Route guards](https://angular.dev/guide/routing/route-guards), angular.dev. [PRIMARIA]
- [HTTP interceptors](https://angular.dev/guide/http/interceptors), angular.dev. [PRIMARIA]
- [Security](https://angular.dev/best-practices/security), angular.dev. [PRIMARIA]
- [provideAppInitializer](https://angular.dev/api/core/provideAppInitializer), angular.dev. [PRIMARIA]
- [CanMatchFn](https://angular.dev/api/router/CanMatchFn), angular.dev. [PRIMARIA]
- [angular/skills, `angular-developer`](https://github.com/angular/skills/tree/main/angular-developer), copia local en `.claude/skills/angular-developer/`. [PRIMARIA]
- `@angular/router` y `@angular/common` 22.2.1, `frontend/node_modules`. [PRIMARIA]
- [Web Locks API](https://developer.mozilla.org/en-US/docs/Web/API/Web_Locks_API), MDN. [PRIMARIA]
- [BroadcastChannel](https://developer.mozilla.org/en-US/docs/Web/API/BroadcastChannel), MDN. [PRIMARIA]
- [Configure Refresh Token Rotation](https://auth0.com/docs/secure/tokens/refresh-tokens/configure-refresh-token-rotation), Auth0. [PRIMARIA del proveedor]
- [Backend For Frontend (BFF) Security Framework](https://docs.duendesoftware.com/bff/), Duende. [OPINIÓN, proveedor]
- [Fix token refresh race with the Web Locks API](https://blog.openreplay.com/fix-token-refresh-race-web-locks-api/), OpenReplay, 2026-09-09. [OPINIÓN]
- [Angular 22 State Management: Signals, SignalStore, or NgRx?](https://abp.io/community/articles/angular-22-state-management-signals-signalstore-or-ngrx-yq8zg0nw), ABP Community. [OPINIÓN]
