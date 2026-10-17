# Convenciones

Reglas para escribir código en este repo. El porqué está en [ADR-0004](adr/0004-vertical-slices-deep-modules.md) y la forma general en [architecture.md](architecture.md).

El código actual todavía no cumple todas estas reglas. La Fase 0 del [plan](evolucion/plan.md) lo lleva hasta ahí. El código nuevo las cumple desde ya.

## Dónde va cada cosa

- Cada capacidad vive en `backend/Features/<Feature>/`. La carpeta de la feature va en plural: `Tasks`, `Users`.
- Cada caso de uso es una slice: una carpeta con el nombre del caso de uso, en verbo + sustantivo (`CreateTask/`, `ListTasks/`). Adentro, `CreateTask.cs` y `CreateTaskTests.cs`.
- La slice es una clase propia con sus dependencias. No hay una clase compartida por todas las slices ni una interface por slice.
- `Contracts/` contiene lo que ven otras features y lo que comparten varias slices. Un archivo por tipo.
- Un tipo que usa una sola slice vive en el archivo de la slice.
- `Domain/` contiene modelos puros y las reglas que comparten las slices. Una regla no se copia entre slices: baja al `Domain`.
- `Persistence/` contiene los mappings de EF.
- `<Feature>Feature.cs` registra la DI y las rutas de la feature. `Program.cs` lista las features de forma explícita.
- Lo que nombra una feature va a esa feature. `Infrastructure/` y `Shared/` no nombran ninguna feature.
- Una feature solo usa el `Contracts/` de otra. Users depende de `Auth.Contracts`; Auth no depende de Users por dentro.

## Nombres y estilo

- El namespace coincide con la ruta, bajo `TemplateApp`. Namespaces file-scoped, nullable e implicit usings.
- Sufijos con significado único: `*Request`, `*Response`, `*Validator`, `*Tests`, `*Feature`.
- Requests y responses son records. La validación de entrada usa FluentValidation. Los mappings de EF usan Fluent API, fuera de `Domain`.
- En el frontend, los modelos usan los mismos nombres que el backend: `*Request` y `*Response`.

## Comentarios

No se escriben comentarios en el código. El porqué de una decisión va en un ADR, en `docs/` o en el mensaje de commit. Si el código necesita un comentario para entenderse, se renombra o se reestructura.

La regla cubre C#, TypeScript, JavaScript, HTML, CSS, XML, YAML y Dockerfile. La hacen cumplir DUNE0009, `Dune.CommentVerifier` y `dune/check-comments.mjs`.

Excepciones:
- Las migraciones generadas por EF.
- Las directivas de herramientas, como `// @ts-expect-error` o `# syntax=`. Las excepciones de herramientas y de migraciones fuera de `Infrastructure/Persistence/Migrations/` llegan con el paso 0.2 del plan.

## HTTP

- Los endpoints adaptan HTTP y devuelven resultados con `ApiResults`. No acceden a EF ni a `UserManager`.
- Una operación carga el recurso por Id antes de decidir propietario o permiso. Primero 404, después 403. La lista propia filtra por `OwnerId`.
- `ErrorOr` Validation produce 400 ValidationProblem; Unauthorized, 401; Forbidden, 403; NotFound, 404; Conflict, 409; Unexpected, 500. Un 500 no publica detalles internos.
- JSON en camelCase. Rutas bajo `/api/v1`.

## Persistencia

- Commits de EF explícitos. Una operación con varias escrituras atómicas abre una transacción antes de escribir y confirma al terminar.
- Se propaga el `CancellationToken` en toda llamada que lo acepte.
- El tiempo sale de `TimeProvider`, en UTC. No se usa el reloj local, ni bloqueos síncronos de `Task`, ni SQL crudo sin parámetros.
- Las migraciones se generan por CLI y son forward, con backfill. No se editan a mano.
- Reintentos, caché y bus entran solo con su paso del plan (29, 33 y 25).

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
