# Convenciones

Reglas para escribir código en este repo. El porqué está en [ADR-0004](adr/0004-vertical-slices-deep-modules.md) y la forma general en [architecture.md](architecture.md).

El código actual todavía no cumple todas estas reglas. La Fase 0 del [plan](evolucion/plan.md) lo lleva hasta ahí. El código nuevo las cumple desde ya.

## Dónde va cada cosa

- Cada capacidad vive en `backend/Features/<Feature>/`. La carpeta de la feature va en plural: `Tasks`, `Users`.
- Cada caso de uso es una slice: una carpeta con el nombre del caso de uso, en verbo + sustantivo (`CreateTask/`, `ListTasks/`). Adentro, un archivo por pieza: `CreateTaskRequest.cs`, `CreateTaskRequestValidator.cs`, `CreateTask.cs`, `CreateTaskEndpoint.cs` y `CreateTaskTests.cs`. Las piezas que el caso de uso no necesita no existen.
- La operación (`CreateTask`) es una clase propia con sus dependencias. El endpoint (`CreateTaskEndpoint`) es estático: expone `Map` y un handler privado que llama a la operación. No hay una clase compartida por todas las slices ni una interface por slice.
- Un archivo, un tipo, en todo el repo: backend, tests, Dune y frontend. Un tipo auxiliar que usa solo otro tipo queda en el archivo de ese tipo, pero el compilador le impide salir: en C# se declara `file` o anidado y privado; en TypeScript, sin `export`. DUNE0010 lo hace cumplir en el backend y FRONT010 en el frontend (un solo `export` de clase, interface, type o enum por archivo). Las migraciones generadas quedan exentas.
- Un tipo que usa una sola slice vive en la carpeta de la slice.
- `Contracts/` contiene lo que ven otras features y las responses HTTP que comparten varias slices.
- Lo interno que comparten varias slices (errores, servicios, mapeos) vive en la raíz de la feature o en una carpeta técnica propia (`Access/`). No va a `Contracts/`.
- `Domain/` contiene modelos puros y las reglas que comparten las slices. Una regla no se copia entre slices: baja al `Domain`.
- `Persistence/` contiene los mappings de EF.
- `<Feature>Feature.cs` registra la DI y las rutas de la feature. `Program.cs` lista las features de forma explícita.
- Lo que nombra una feature va a esa feature. `Infrastructure/` y `Shared/` no nombran ninguna feature.
- Una feature solo usa el `Contracts/` de otra. Users depende de `Auth.Contracts`; Auth no depende de Users por dentro. DUNE0004 lo hace cumplir: mira el tipo nombrado y también el tipo de cada expresión, así que `var` no lo esquiva.
- Entre features, una entidad guarda solo el Id de la otra, sin propiedad de navegación. Si necesita un dato de la otra feature, lo pide a su contrato.

## Nombres y estilo

- El namespace coincide con la ruta, bajo `TemplateApp`. Namespaces file-scoped, nullable e implicit usings.
- Sufijos con significado único: `*Request`, `*Response`, `*Validator`, `*Endpoint`, `*Tests`, `*Feature`. No se usan `*Module` ni `*Endpoints` en features. DUNE0008 lo hace cumplir.
- Requests y responses son records. La validación de entrada usa FluentValidation. Los mappings de EF usan Fluent API, fuera de `Domain`.
- En el frontend, los modelos usan los mismos nombres que el backend: `*Request` y `*Response`. Cada uno va en su archivo: `task-response.ts` / `TaskResponse`.
- En el frontend, cada feature publica en `features/<feature>/index.ts` lo que ven las demás y la raíz de la app. Fuera de una feature, se importa solo su `index.ts`. FRONT001 lo hace cumplir, también para `app.routes.ts` y `app.config.ts`.
- Las rutas de una feature van en `<feature>.routes.ts`, sin guards. `app.routes.ts` las compone y aplica los guards.
- En la raíz de una feature del frontend solo viven `index.ts` y `<feature>.routes.ts`. Lo demás va en una carpeta con nombre de propósito: `api/` para el cliente HTTP y sus modelos, `session/` para la sesión de Auth y una carpeta por componente. No hay carpetas por tipo técnico (`services/`, `models/`). FRONT011 lo hace cumplir.
- Un componente del frontend es **de ruta** o **de presentación**. El de ruta es el destino de una navegación: lo carga `loadComponent` o `component` en un `*.routes.ts`, o es la raíz `App` que arranca `main.ts`. Orquesta: inyecta servicios, llama a la API y decide. El de presentación recibe datos con `input()` y avisa con `output()`: no inyecta servicios de la app (nada de `frontend/src/app/**`), ni `Router` ni `ActivatedRoute`. Sí puede inyectar `ElementRef`, `Injector` o `DestroyRef`. FRONT013 lo hace cumplir: mira el tipo que devuelve cada `inject()` y el de cada parámetro del constructor, así un alias no lo esquiva.
- Cada componente vive en su carpeta, con el nombre de su propósito, dentro de su feature: `features/tasks/tasks/` (de ruta) y `features/tasks/task-pager/` (de presentación). No hay carpetas `components/` ni `containers/`: el tipo de componente lo decide cómo se usa, no la carpeta. La skill `angular-developer` propone `components/`, pero la guía de estilo de Angular y este repo la descartan.
- El estado propio de la UI vive en el componente que lo usa. El borrador de una fila es un `linkedSignal` de `TaskRow` que se reinicia cuando cambia la tarea o se abre la edición. Lo que coordina varias piezas queda en el componente de ruta: `editingId` vive en `Tasks`, así edita una sola fila a la vez.
- Un componente de presentación maneja solo su propio DOM, con `viewChild` y `afterRenderEffect`: `TaskDeleteDialog` abre y cierra su `<dialog>`. Lo que cruza componentes, como el foco entre una fila y el encabezado, lo decide el componente de ruta.
- `core/` del frontend no nombra ninguna feature: queda para lo transversal.
- En el frontend, los archivos y las clases se nombran por su propósito, sin sufijo de tipo (*Intent over Role*, Angular v20+): `session.ts` / `Session`, `tasks-api.ts` / `TasksApi`, `profile.ts` / `Profile`. Guards e interceptores usan guion: `auth-guard.ts`. Se mantienen las convenciones del propio Angular: `*.routes.ts`, `app.config.ts`, `*.spec.ts`. FRONT004 lo hace cumplir.
- Los servicios de una sola instancia usan `@Service()` (Angular v22), no `@Injectable({ providedIn: 'root' })`. FRONT005 lo hace cumplir.
- Un servicio guarda su estado en un `signal` privado y expone solo `computed()` o `.asReadonly()`. Nadie de afuera llama a `set()`: el servicio ofrece acciones con nombre (`session.open()`, `session.replaceUser()`). FRONT012 rechaza una propiedad pública de tipo `WritableSignal` en una clase con `@Service()` o `@Injectable()`.
- La sesión del frontend usa los verbos del backend y de CONTEXT.md: `open`, `close`, más `restore` al recargar. Su estado es uno solo: `restoring`, `open` o `closed`. El HTTP de Auth vive en `auth/api/auth-api.ts` (`AuthApi`), como `UsersApi` y `TasksApi`.
- El frontend no guarda, no lee y no manda tokens: no hay header `Authorization`. La sesión es la cookie `__Host-session`, que el navegador manda solo. El interceptor solo mira las respuestas de `/api/`: ante un 401 con la sesión abierta, `Session.end()` cierra la sesión y lleva a `/login?returnUrl=…`. Las peticiones de `AuthApi` van marcadas con el `HttpContextToken` `SKIP_SESSION` y el interceptor no las toca: un 401 del login es un error del formulario. El error llega tal cual al componente.
- `returnUrl` lleva solo rutas internas: empieza con `/` y no con `//`. Cualquier otro valor lleva a `/tasks`. Así el login no redirige a otro sitio.
- Las pestañas del mismo navegador comparten la cookie de sesión: no hay renovación ni lock entre pestañas. `Session` avisa `opened` y `closed` por `BroadcastChannel('session')`. Con `closed`, las otras pestañas cierran la sesión local y van a `/login`. Con `opened`, la restauran.
- Los formularios usan Signal Forms (`@angular/forms/signals`): un `signal` con el modelo, `form()` con las reglas (`required`, `maxLength`…) y `submission.action` para enviar. El `<form>` lleva `[formRoot]` y cada input `[formField]`. Las reglas escriben los atributos `required` y `maxlength`: no se ponen a mano. Un campo suelto, sin `<form>`, también usa Signal Forms: la edición en línea de `TaskRow` tiene su `form()` y envía con `submit()`. El error de un campo va justo después de su input, en rojo (`text-red-800`), y el input lo nombra con `aria-describedby`. El error de una acción contra la API va en un `role="alert"` de la página. No se leen inputs con `$any($event.target)`. FRONT006 rechaza importar `@angular/forms`.
- La inicialización usa providers funcionales: `provideAppInitializer()`, `provideEnvironmentInitializer()` y `providePlatformInitializer()`, no los tokens `APP_INITIALIZER`, `ENVIRONMENT_INITIALIZER` ni `PLATFORM_INITIALIZER`. FRONT007 lo hace cumplir.
- El formato del frontend lo decide Prettier con `frontend/.prettierrc` (100 columnas, comillas simples, parser `angular` para los `.html`). `npm run format:check` es un gate del README y de CI; `npm run format` arregla. No se formatea a mano ni se discute el formato en una revisión. Los `.mjs` de `dune/` quedan fuera: no hay configuración de Prettier que los cubra.
- Cada componente separa vista y lógica: `tasks.ts` declara `templateUrl: './tasks.html'`. No se usan `template` ni `styles` en línea. FRONT008 lo hace cumplir.
- Los estilos son clases de Tailwind v4 en el template: `class="rounded-lg border border-slate-200 bg-white p-5"`. No hay `.css` por componente ni `style=""`. Se repiten las clases en lugar de inventar clases con `@apply`. `src/styles.css` guarda solo `@import 'tailwindcss'`, los tokens de `@theme` y el foco visible por defecto (`@layer base`). FRONT009 rechaza `styleUrl` y `styleUrls`.
- El CLI genera código que el gate acepta. `angular.json` fija `"style": "none"` en `@schematics/angular:component`: `ng generate component` no crea `.css` ni `styleUrl`. FRONT014 lo hace cumplir.

## Comentarios

No se escriben comentarios en el código. El porqué de una decisión va en un ADR, en `docs/` o en el mensaje de commit. Si el código necesita un comentario para entenderse, se renombra o se reestructura.

La regla cubre C#, TypeScript, JavaScript, HTML, CSS, XML, YAML y Dockerfile. La hacen cumplir DUNE0009, `Dune.CommentVerifier` y `dune/check-comments.mjs`.

Excepciones (la lista es cerrada):
- Las migraciones generadas por EF, en `backend/Infrastructure/Persistence/Migrations/` o en `backend/Features/<Feature>/Persistence/Migrations/`. Una carpeta `Migrations/` en otro lugar no queda exenta.
- `// @ts-expect-error` sola, sin texto. Con texto, en bloque `/* */` o como `@ts-ignore`, cuenta como comentario.
- Las directivas del parser de Docker (`# syntax=`, `# escape=`, `# check=`), solo al principio del Dockerfile. Después de una línea vacía, un comentario o una instrucción, cuentan como comentario, igual que en Docker.

## HTTP

- Los endpoints adaptan HTTP y devuelven resultados con `ApiResults`. No acceden a EF ni a `UserManager`.
- Una operación carga el recurso por Id antes de decidir propietario o permiso. Primero 404, después 403. La lista propia filtra por `OwnerId`.
- `ErrorOr` Validation produce 400 ValidationProblem; Unauthorized, 401; Forbidden, 403; NotFound, 404; Conflict, 409; Unexpected, 500. Un 500 no publica detalles internos.
- JSON en camelCase. Rutas bajo `/api/v1`.

## Persistencia

- Las FKs entre features se declaran en `AppDbContext.ConfigureCrossFeatureRelationships` hasta el paso 22. La `Persistence` de una feature configura solo sus propias tablas.
- Commits de EF explícitos. Una operación con varias escrituras atómicas abre una transacción antes de escribir y confirma al terminar.
- Se propaga el `CancellationToken` en toda llamada que lo acepte.
- El tiempo sale de `TimeProvider`, en UTC. No se usa el reloj local, ni bloqueos síncronos de `Task`, ni SQL crudo sin parámetros.
- Las migraciones se generan por CLI y son forward, con backfill. No se editan a mano.
- Reintentos, caché y bus entran solo con su paso del plan (29, 33 y 25). Hasta el paso 25, `check-structure.mjs` rechaza cualquier librería de bus o mediator (MediatR, MassTransit, Wolverine, NServiceBus, Rebus y otras), sea paquete, `<Using>` o `using`.

## Seguridad

- El propietario nunca viene del cliente.
- Roles y permisos no van en la sesión. Se consultan en cada request.
- La sesión vive en el servidor. El navegador solo guarda `__Host-session` (`HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`). Ningún body ni cookie legible por JavaScript lleva un token de sesión; la única cookie legible es `XSRF-TOKEN`. Las credenciales no se guardan en el storage del navegador.
- Antiforgery se valida en toda escritura de la API (`POST`, `PUT`, `PATCH`, `DELETE` bajo `/api`), también para anónimos. Lo hace un middleware de Auth, no cada endpoint: un endpoint nuevo queda protegido sin pedirlo.
- No se loguean bodies, `Authorization`, `Cookie`, contraseñas ni tokens. Ids y títulos no se usan como etiquetas de métricas.
- No se versionan secretos ni connection strings con credenciales.

## Pruebas

- Las pruebas de una slice cruzan HTTP con `WebApplicationFactory` y PostgreSQL real (Testcontainers). No se usa EF InMemory ni mocks que solo repiten respuestas.
- El soporte compartido de pruebas vive en `backend/Tests/Support`. Los helpers no se copian entre archivos de test.
- Las pruebas de `Infrastructure/` y del arranque del host viven en `backend/Tests/<Área>/`, por ejemplo `backend/Tests/Startup/`.
- Se prueba por comportamiento: autorización, transacciones, sesión revocada o vencida, antiforgery, cascadas, health y exportación de señales.
- Las reglas de Dune se prueban con una fixture válida y una violación deliberada, observando diagnósticos o códigos de salida.
- En el frontend, cada `*.spec.ts` vive junto a su archivo y corre con Vitest y jsdom. Se prueban funciones puras y componentes de presentación: entradas con `fixture.componentRef.setInput()`, salidas con `subscribe()` y el DOM después de `await fixture.whenStable()` ("Act, Wait, Assert"). No se usa `fixture.detectChanges()`. Un componente de ruta se prueba en el navegador, contra la API real.
- Las pruebas e2e viven en `frontend/e2e/`, una spec por feature, con Playwright Test. Buscan por rol y por label (`getByRole`, `getByLabel`), no por clases ni ids, salvo para medir algo que no tiene rol (el foco o el centro del diálogo). Corren en serie contra una instancia de `verify-template` (`E2E_BASE_URL`) y respetan el limiter de `/auth` con `waitForAuthRoom()`. Los helpers compartidos van en `e2e/support/`.

## Cambios

- Un paso del plan = una rama y un PR, con un solo cambio observable. El merge lo hace Tobi.
- Si cambia un contrato HTTP, el PR incluye backend, frontend y tests.
- No se desactivan analyzers, no se omiten tests y CI no usa `continue-on-error`.
- Una regla de Dune no se cambia para tapar una falla. Sí se cambia si ese cambio es el objetivo del paso: con un ADR o una línea en `decisions.tsv`, y con su fixture válida y su violación deliberada.
- Los comandos de verificación están en [README.md](../README.md#ejecutar-gates).
