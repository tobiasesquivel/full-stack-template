# Convenciones del monorepo

Estas convenciones acompañan [ADR-0003](adr/0003-full-stack-template-dune.md) y los [contratos activos](../openspec/specs/dune/spec.md). Sustituyen `backend/docs/AspNetConventions.md` y `project-goals.md`. Verificación real 2026-10-05: backend 24/24 pass (reporte parent artifact://611), Docker `up --build` final con éxito, smoke `--compose` exit 0 y navegador PASS. Dune 22/22 PASS local (artifact://640) con gate duro real DUNE0001 en throwaway y `dotnet build TemplateApp.slnx` exit 0, 0 errores (artifact://643). GitHub Actions no ejecutado; sin afirmación de cero warnings. No son una lista de capacidades pendientes ni evidencia por sí solas de que los gates hayan pasado.

## Localidad y nombres

- Cada capacidad vive en `backend/Features/<Name>`. Modelos puros viven en `Domain`, mappings en `Persistence`, contratos compartidos en `Contracts` y pruebas en `Tests`. Requests y validators locales pueden vivir junto al caso de uso.
- `*Module` implementa la operación. `*Endpoints` adapta HTTP. `*Request`, `*Response`, `*Validator` y `*Tests` tienen un significado único.
- Namespace coincide con la ruta bajo `TemplateApp`. Usa namespaces file-scoped, nullable e implicit usings.
- Requests y responses son records. Validación de entrada usa FluentValidation. Mappings EF usan Fluent API fuera de Domain.
- No hay handlers de bus, controllers parciales, wrappers genéricos de repositorio ni aliases de APIs retiradas.
- Coloca documentación normativa en `docs` y contratos activos solo en `openspec/specs`. `CONTEXT.md` contiene únicamente vocabulario del dominio.

Tarea es el patrón de casos de uso colocados. Un archivo por operación contiene las declaraciones parciales de `TasksModule` y `TasksEndpoints`. Crear y actualizar contienen además sus requests y validators. No agregues un módulo ni registro por archivo. Mantén un solo constructor con las dependencias compartidas y un solo mapa de rutas. Coloca cliente HTTP y modelos exclusivos de Tarea en la feature Angular, no en Core.

## Adaptación HTTP y persistencia

Endpoints llaman módulos y adaptan ErrorOr mediante `ApiResults`. No acceden a EF, Persistence o UserManager. Módulos cargan recursos individuales por Id antes de owner-or-permission. Separa ausencia 404 de prohibición 403. La lista propia sí filtra OwnerId.

DUNE0002 aplica también al tipo endpoint dentro de archivos mixtos, incluidos tipos anidados. EF en `TasksModule` no autoriza su uso en el adapter vecino. No renombres el archivo para eludir la regla.

ErrorOr Validation produce 400 ValidationProblem. Unauthorized produce 401, Forbidden 403, NotFound 404, Conflict 409 y Unexpected 500. No publiques detalles internos en errores 500. Usa JSON camelCase y `/api/v1`.

Usa commits EF explícitos. Una operación con varias escrituras que debe ser atómica abre una transacción antes de escribir y confirma solo al terminar. Identity participa del mismo contexto scoped. No agregues retries generales ni infraestructura de cache global. Propaga CancellationToken en llamadas que lo aceptan.

Usa `TimeProvider` para tiempo UTC. No uses reloj local, bloqueo síncrono de Task ni SQL raw sin parámetros. SQL interpolado parametrizado permite locks cuando la operación realmente los necesita. Conserva migraciones históricas y agrega migraciones forward con backfill.

## Seguridad y señales

- No aceptes propietario desde el cliente.
- No hornees roles o permisos en JWT. Consulta permisos scoped en cada request.
- No expongas refresh tokens en JSON ni guardes credenciales en storage del navegador.
- Valida antiforgery en register, login, refresh y logout, también anónimos.
- No registres bodies, Authorization, Cookie, contraseñas ni tokens. No uses IDs o títulos como etiquetas de métricas.
- No incluyas secrets ni connection strings con credenciales en archivos versionados. La configuración requiere entorno o secret store.

## Pruebas y PRs

Pruebas de feature cruzan HTTP con WebApplicationFactory y PostgreSQL real. El soporte compartido vive en `backend/Tests/Support`. No uses EF InMemory ni mocks que solo repiten respuestas. Prueba autorización, transacciones, replay, cascadas, health y exportación por comportamiento.

`TestSession` concentra cookies, antiforgery, Bearer, JSON y envío HTTP. Cada sesión conserva su propio cliente y cookie jar. Las pruebas de cada feature conservan las expectativas de estados, payloads y reglas. Las pruebas de carreras copian solo el refresh token y mantienen tokens antiforgery independientes. `ApiFixture.CreateIsolatedClient` separa el rate limiter por aplicación sin sustituir PostgreSQL.

Pruebas de analyzer compilan casos válidos y violaciones deliberadas y observan diagnósticos. Pruebas de estructura usan fixtures y códigos de salida. No reemplaces la prueba por búsquedas de nombres o cadenas del source.

El inventario de artefactos versionados usa `git ls-files --cached`. Un archivo ignorado agregado al índice falla, incluso si ya se borró del directorio de trabajo. Una caché local ignorada y no indexada no falla. La detección de repositorios anidados es una comprobación separada. Si Git no puede entregar el inventario, el gate falla.

Un PR contiene un cambio observable completo. Si cambia un contrato, incluye backend, frontend, tests y OpenSpec juntos. No impongas un límite de líneas. No desactives analyzers, no omitas pruebas ni uses continue-on-error para conseguir CI verde. Describe checks ejecutados y bloqueos reales por separado.

Los [comandos de gates](../README.md#ejecutar-gates) están en README. Comandos vigentes: `node --test tools/check-structure.test.mjs` (path explícito), `dotnet test TemplateApp.slnx` con runner MTP (proyectos backend `Tests` y `tools/Dune.Analyzers.Tests`), sin suite frontend vigente (`*.spec.ts` ausente; prueba real en navegador). Migraciones solo por CLI (`InitialCreate` en commit `6687af8`); original no tocado. No conviertas reportes heredados en evidencia del estado actual.
