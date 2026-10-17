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
- Lo interno que comparten varias slices (errores, servicios, mapeos) vive en la raíz de la feature o en una carpeta técnica propia (`Access/`, `Jwt/`). No va a `Contracts/`.
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
- `core/` del frontend no nombra ninguna feature: queda para lo transversal.
- En el frontend, los archivos y las clases se nombran por su propósito, sin sufijo de tipo (*Intent over Role*, Angular v20+): `session.ts` / `Session`, `tasks-api.ts` / `TasksApi`, `profile.ts` / `Profile`. Guards e interceptores usan guion: `auth-guard.ts`. Se mantienen las convenciones del propio Angular: `*.routes.ts`, `app.config.ts`, `*.spec.ts`. FRONT004 lo hace cumplir.
- Los servicios de una sola instancia usan `@Service()` (Angular v22), no `@Injectable({ providedIn: 'root' })`. FRONT005 lo hace cumplir.
- Los formularios usan Signal Forms (`@angular/forms/signals`): un `signal` con el modelo, `form()` con las reglas (`required`, `maxLength`…) y `submission.action` para enviar. El `<form>` lleva `[formRoot]` y cada input `[formField]`. Las reglas escriben los atributos `required` y `maxlength`: no se ponen a mano. FRONT006 rechaza importar `@angular/forms`.
- La inicialización usa providers funcionales: `provideAppInitializer()`, `provideEnvironmentInitializer()` y `providePlatformInitializer()`, no los tokens `APP_INITIALIZER`, `ENVIRONMENT_INITIALIZER` ni `PLATFORM_INITIALIZER`. FRONT007 lo hace cumplir.
- Cada componente separa vista y lógica: `tasks.ts` declara `templateUrl: './tasks.html'`. No se usan `template` ni `styles` en línea. FRONT008 lo hace cumplir.
- Los estilos son clases de Tailwind v4 en el template: `class="rounded-lg border border-slate-200 bg-white p-5"`. No hay `.css` por componente ni `style=""`. Se repiten las clases en lugar de inventar clases con `@apply`. `src/styles.css` guarda solo `@import 'tailwindcss'`, los tokens de `@theme` y el foco visible por defecto (`@layer base`). FRONT009 rechaza `styleUrl` y `styleUrls`.

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
- Roles y permisos no van en el JWT. Se consultan en cada request.
- Los refresh tokens no viajan en JSON. Las credenciales no se guardan en el storage del navegador.
- Antiforgery se valida en register, login, refresh y logout, también para anónimos.
- No se loguean bodies, `Authorization`, `Cookie`, contraseñas ni tokens. Ids y títulos no se usan como etiquetas de métricas.
- No se versionan secretos ni connection strings con credenciales.

## Pruebas

- Las pruebas de una slice cruzan HTTP con `WebApplicationFactory` y PostgreSQL real (Testcontainers). No se usa EF InMemory ni mocks que solo repiten respuestas.
- El soporte compartido de pruebas vive en `backend/Tests/Support`. Los helpers no se copian entre archivos de test.
- Las pruebas de `Infrastructure/` y del arranque del host viven en `backend/Tests/<Área>/`, por ejemplo `backend/Tests/Startup/`.
- Se prueba por comportamiento: autorización, transacciones, replay, cascadas, health y exportación de señales.
- Las reglas de Dune se prueban con una fixture válida y una violación deliberada, observando diagnósticos o códigos de salida.

## Cambios

- Un paso del plan = una rama y un PR, con un solo cambio observable. El merge lo hace Tobi.
- Si cambia un contrato HTTP, el PR incluye backend, frontend y tests.
- No se desactivan analyzers, no se omiten tests y CI no usa `continue-on-error`.
- Una regla de Dune no se cambia para tapar una falla. Sí se cambia si ese cambio es el objetivo del paso: con un ADR o una línea en `decisions.tsv`, y con su fixture válida y su violación deliberada.
- Los comandos de verificación están en [README.md](../README.md#ejecutar-gates).
