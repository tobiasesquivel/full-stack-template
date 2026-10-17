# Plan progresivo: llevar este backend hacia el nivel empresarial del informe

**Propósito**: usar este repo como laboratorio de práctica. El objetivo no es llegar a microservicios, sino **dominar una escalera**: cada paso agrega una pieza real, la verifica con los gates que ya existen, y deja evidencia. Al final tenés criterio para decidir en cualquier volumen.

**Cómo leerlo**: una Fase 0 de bases y 10 fases con 36 pasos en orden creciente de dificultad. Cada paso tiene tres líneas fijas: **Concepto** (qué se aprende), **Cambia** (áreas y archivos), **Verifica** (gate o test del repo que lo prueba). Nada de código en este plan: los pasos describen *qué* cambia y *cómo* se prueba.

---

## 1. Estado real de partida (verificado en el código)

### Lo que existe hoy

Es el punto de partida, no un límite. Cualquier pieza puede cambiar si mejora el rigor técnico o el aprendizaje.

| Área | Estado real |
|---|---|
| Estilo | Features `Auth`, `Users`, `Tasks`, `Health`, sin mediator ni bus. Desde el paso 0.4, cada caso de uso es una slice en su carpeta (request, validador, operación, endpoint y test). La Fase 0 lleva el resto a la forma de [ADR-0004](../adr/0004-vertical-slices-deep-modules.md) |
| Contratos | `Features/*/Contracts`, `Features/*/Domain`, `Features/*/Persistence`; `<Slice>Tests.cs` junto a cada slice, excluidos de producción por `Compile Remove`. El contrato HTTP lo fijan los tests de integración |
| Errores | `Shared/Http/ApiResults` traduce `ErrorOr` a ProblemDetails (400/401/403/404/409/500) |
| Autorización | `IPermissionService` + `ResourceAuthorizer`; permisos consultados por request; orden 404 antes de 403 |
| Seguridad | JWT con validación de sesión activa, refresh rotativo en cookie HttpOnly, antiforgery, lockout de Identity, rate limiting de ventana fija **solo** en los 4 endpoints de `/auth` |
| Observabilidad | OpenTelemetry (trazas + métricas) con exporter OTLP y meters declarados: `Npgsql`, `TemplateApp.Api`, `TemplateApp.Business`; Serilog a consola y OTLP; collector en `deploy/otel-collector-config.yaml`; señales en `otel-export/*.json` |
| Salud | `/api/v1/health/live` y `/ready` (base + migraciones) |
| Datos | `AppDbContext` único, una migración `InitialCreate`, `DatabaseInitializer`, EF Core + Npgsql |
| Reglas ejecutables | Dune: analizadores DUNE0001-0009 con severidad error, `BannedSymbols.txt`, `Dune.CommentVerifier`, y los gates Node `check-structure.mjs`, `check-comments.mjs`, `check-frontend.mjs`. Cada regla tiene fixture válida y violación deliberada. El paso 0.2 corrigió `.editorconfig` y cambió el gate de Wolverine por un gate neutral de bus |
| Pruebas backend | MTP + xunit.v3 + Testcontainers PostgreSQL real; `ApiFixture` y `TestSession` en `backend/Tests/Support` |
| CI | `.github/workflows/ci.yml` con 4 jobs (backend, frontend, comentarios, compose + smoke con evidencia OTLP) |
| Verificación runtime | Skill del proyecto `verify-template`: `launch` / `doctor` / `api` / `cleanup` con artefactos en `artifacts/verify-template/<runId>/` |
| Frontend | Angular 22, **sin suite de tests**: la prueba real es el navegador |

### Lo que falta (los huecos que este plan llena, en este orden)

Nada de bus, outbox, eventos de dominio ni sagas. Nada de caché. Ningún trabajo programado. Sin idempotencia de escritura. Sin control de concurrencia propio en `TaskItem`. Sin auditoría. Sin librería de versionado de API. Validación de configuración por excepciones manuales. Sin cliente HTTP saliente ni resiliencia. Sin SLOs ni tableros. Sin SBOM, escaneo de dependencias ni de secretos. Un solo `DbContext` para todo el sistema. Sin despliegue progresivo.

---

## 2. Reglas de avance

Las reglas viven en [AGENTS.md](../../AGENTS.md#reglas-de-avance). Este plan no las repite.

---

## 3. Protocolo de verificación por paso

Los comandos viven en [README.md](../../README.md#ejecutar-gates). Cada paso indica abajo cuáles aplican.

Regla de oro: **un paso no termina con tests verdes.** Termina cuando el caso que agrega se ejercita de verdad: por HTTP con `smoke-api.mjs`, o en el navegador si hay superficie visible.

---

## 4. El plan

### Fase 0 — Bases (pasos 0.0-0.12)

Llevan el repo a la forma de [ADR-0004](../adr/0004-vertical-slices-deep-modules.md). Ningún paso cambia un contrato HTTP: el smoke de la API prueba que nada se rompió.

**0.0. Inicialización concurrente de la base.** (adelantado del paso 3)
**Concepto**: inicialización concurrente; serializar con un advisory lock de PostgreSQL.
**Cambia**: `DatabaseInitializer` toma un advisory lock antes de migrar y sembrar; `ApiFixture` deja de sembrar; test nuevo que corre 8 inicializaciones a la vez.
**Verifica**: el test nuevo falla sin el lock y pasa con él; la suite completa pasa con una sola base compartida, como en CI.

**0.1. Docs y reglas de trabajo.**
**Concepto**: una sola fuente de verdad por regla; decisiones registradas en ADR.
**Cambia**: ADR-0004, `architecture.md`, `conventions.md`, `AGENTS.md`, `README.md`, este plan, `decisions.tsv` desde cero, notas en los informes, limpieza de referencias viejas. Nada de código.
**Verifica**: ningún link relativo roto en los `.md`; ninguna referencia a OpenSpec, `artifact://` ni a ADR borrados; gates de Node en verde.

**0.2. Arreglos de Dune.**
**Concepto**: un gate que no se aplica, o que elige por vos, es peor que no tenerlo.
**Cambia**: `.editorconfig` (las severidades quedaron bajo `[*.md]`); gate de bus neutral en lugar del gate de Wolverine; excepciones de comentarios para migraciones de cualquier feature y para directivas de herramientas. Cada regla con fixture válida y violación deliberada.
**Verifica**: build y tests de Dune; la violación deliberada de cada regla nueva falla con el diagnóstico esperado.

**0.3. Reparto del código.**
**Concepto**: lo que nombra una feature vive en esa feature; cada feature es su propio composition root.
**Cambia**: JWT, `AppIdentityUser`, `JwtOptions`, roles, `RoleSeeder` y `PermissionService` + `ResourceAuthorizer` (fusionados en `IAccessPolicy`) a `Features/Auth`; permisos al `Contracts` de cada feature; `<Feature>Feature.cs` en cada feature; DUNE0003; se borra el código muerto (`ITemplateMetrics`, `ValidationExceptionHandler`, `UserErrors.InvalidTimezone`). Ver [informe-vsa-deep-modules.md](informe-vsa-deep-modules.md) §2.
**Verifica**: build, tests y smoke HTTP sin cambios de comportamiento.

**0.4. Slices.**
**Concepto**: vertical slice como clase propia; feature como módulo profundo.
**Cambia**: una carpeta y una clase por caso de uso en Tasks, Users, Auth y Health, con su test; las reglas comunes bajan al `Domain`; DUNE0008 y `check-structure.mjs`.
**Verifica**: build, tests y smoke HTTP sin cambios de comportamiento; fixtures de DUNE0008 con la forma nueva.

**0.5. Límite entre Auth y Users.**
**Concepto**: dependencia en una sola dirección entre módulos; contrato público.
**Cambia**: Users usa solo `Auth.Contracts`; Auth crea el perfil mediante un contrato de Users; DUNE0004 permite solo `Contracts`. Absorbe la mayor parte del paso 21.
**Verifica**: build, tests y smoke HTTP; fixture negativa que usa el `Domain` de otra feature y falla.

**0.6. Frontend por feature.**
**Concepto**: el mismo límite de módulo, en el cliente.
**Cambia**: servicios, modelos y rutas de Auth y Users a su feature; `core/` solo transversal; `*Payload` pasa a `*Request`; `check-frontend.mjs` si hace falta.
**Verifica**: build de Angular, `check-frontend` y navegador: registro, login, recarga, perfil, tareas y logout.

Los pasos 0.7 a 0.9 llevan el frontend a la forma de Angular v22. Se agregaron el 2026-10-17, a pedido de Tobi. La investigación está en [informe-angular-moderno.md](informe-angular-moderno.md).

**0.7. Servicios y nombres de Angular v22.**
**Concepto**: *Intent over Role*: el nombre dice el propósito, no el tipo técnico; `@Service()` para los servicios de una sola instancia.
**Cambia**: los servicios, guards e interceptores del frontend pierden el sufijo de tipo y pasan a `@Service()`; una regla nueva en `check-frontend.mjs` con su fixture válida y su violación deliberada.
**Verifica**: build de Angular, `check-frontend` y navegador con el mismo recorrido del paso 0.6.

**0.8. Signal Forms.**
**Concepto**: el estado del formulario como signals (`@angular/forms/signals`), estable desde v22.
**Cambia**: los formularios de login, registro, tareas y perfil pasan de Reactive Forms a Signal Forms, con los mismos ids, mensajes y validaciones.
**Verifica**: build de Angular, `check-frontend` y navegador: cada formulario acepta lo válido y muestra el mismo error con lo inválido.

**0.9. Arranque con `provideAppInitializer()`.**
**Concepto**: providers funcionales en lugar de tokens multi.
**Cambia**: `app.config.ts` reemplaza `APP_INITIALIZER` por `provideAppInitializer()`.
**Verifica**: build de Angular y navegador: la recarga restaura la sesión y una ruta protegida sin sesión lleva a `/login`.

Los pasos 0.10 y 0.11 ordenan los archivos. Se agregaron el 2026-10-17, a pedido de Tobi.

**0.10. Componentes en archivos separados.**
**Concepto**: separar la vista de la lógica; cada archivo cambia por un solo motivo.
**Cambia**: login, registro, tareas y perfil pasan su `template` a `<componente>.html` y sus `styles` a `<componente>.css`, como `app.ts`; una regla nueva en `check-frontend.mjs` con su fixture válida y su violación deliberada.
**Verifica**: build de Angular, `check-frontend` y navegador con el mismo recorrido del paso 0.8.

**0.11. Un archivo, un tipo.**
**Concepto**: un archivo por tipo en todo el repo; un tipo auxiliar que usa solo otro tipo queda en su archivo, pero el compilador le impide salir de ahí.
**Cambia**: los modelos del frontend y `Tests/Support/TestSession.cs` se separan; un analyzer nuevo de Dune y una regla nueva en `check-frontend.mjs`, cada uno con su fixture válida y su violación deliberada.
**Verifica**: build y tests .NET, tests de Dune, build de Angular, `check-frontend`, smoke HTTP y navegador.

El paso 0.12 se agregó el 2026-10-17, a pedido de Tobi: "migrá de CSS a Tailwind".

**0.12. Estilos con Tailwind.**
**Concepto**: CSS utility-first: el estilo vive en clases del template y no en hojas por componente; el CSS global queda solo para lo que no es una clase.
**Cambia**: los templates de app, login, registro, tareas y perfil usan clases de Tailwind v4 con los mismos colores (la paleta más cercana); se borran `app.css`, `tasks.css`, los `styleUrl` y los `style=""` del diálogo; `styles.css` queda con `@import 'tailwindcss'`, un breakpoint y el foco visible; una regla nueva en `check-frontend.mjs` (FRONT009) con su fixture válida y su violación deliberada.
**Verifica**: build de Angular, `check-frontend`, capturas antes y después de cada vista, y navegador con el recorrido del paso 0.10 adaptado a las clases nuevas.

### Fase 1 — Base verificable (pasos 1-3)

**1. Línea base: qué funciona hoy, con evidencia.**
**Concepto**: verificación reproducible; separar "compila" de "funciona"; PASS/FAIL/NOT_RUN.
**Cambia**: nada de producto. Solo `artifacts/verify-template/<runId>/` y una nota de estado en `docs/`.
**Verifica**: los gates del README + `launch`/`api`/`cleanup` de la skill, con estado por etapa.

**2. Un solo comando de verificación local.**
**Concepto**: pipeline local reproducible; una entrada, no veinte comandos recordados.
**Cambia**: `.agents/skills/verify-template/scripts/` (wrapper que orquesta los gates existentes; **no** duplicar `smoke-api.mjs`).
**Verifica**: el comando nuevo produce el mismo resultado que los comandos sueltos, y `node dune/check-structure.mjs` sigue en verde (sin herramientas duplicadas).

**3. CI verde de verdad.**
**Concepto**: integración continua como gate efectivo, no como archivo versionado.
**Nota**: el paso 0.0 ya arregló la falla de siembra concurrente que tumbaba el job de backend.
**Cambia**: `ci.yml` solo si algún job falla; `docs/` con el estado real; `artifacts/`.
**Verifica**: un PR de prueba dispara los 4 jobs; el job `compose` deja el artifact `otel-export` descargable.

### Fase 2 — Contratos de la API (pasos 4-7)

**4. Versionado de API declarado.**
**Concepto**: evolución de contratos públicos; `/api/v1` deja de ser un string literal y pasa a ser una versión gestionada.
**Cambia**: agrupaciones de endpoints de cada feature, tests de contrato.
**Verifica**: `dotnet test TemplateApp.slnx`; `smoke-api.mjs` responde igual en v1; `check-structure.mjs` en verde.

**5. Validación de configuración al arranque.**
**Concepto**: fail-fast; la configuración es un contrato, no un supuesto.
**Cambia**: options de cada feature (por ejemplo `JwtOptions` en Auth) y del host, `appsettings.example.json`, tests de arranque.
**Verifica**: nuevo test que arranca el host con configuración inválida y espera fallo con mensaje claro; `dotnet test` completo.

**6. Catálogo de errores estable.**
**Concepto**: el contrato de error también es API; hoy los códigos se arman en cada caso de uso.
**Cambia**: `Shared/Http`, catálogos por feature, tests de estados y payloads.
**Verifica**: test de cada tipo de error (400/401/403/404/409) con `code` estable; `smoke-api.mjs`.

**7. Paginación por keyset.**
**Concepto**: lectura estable a escala; el offset se degrada con volumen y duplica filas al insertar.
**Cambia**: la slice `ListTasks`, `Contracts`, cliente y modelos de tareas en el frontend, tests.
**Verifica**: test que inserta filas entre dos páginas y no repite ni omite; navegador (`verify-template` drive) sobre la lista paginada.

### Fase 3 — Escritura correcta (pasos 8-10)

**8. Concurrencia optimista en `TaskItem`.**
**Concepto**: lost update; el estado final explícito no alcanza sin control de versión.
**Cambia**: `Domain/TaskItem.cs`, `Persistence/TaskItemConfiguration.cs`, la slice `UpdateTask`, migración forward, tests.
**Verifica**: test con dos actualizaciones concurrentes: una gana y la otra recibe 409; migración aplicada por CLI sin editar a mano.

**9. Idempotencia de escritura.**
**Concepto**: reintentos seguros; a todo POST de negocio se le puede repetir la intención.
**Cambia**: mecanismo genérico en `Infrastructure/`, la slice `CreateTask`, migración de la tabla de claves, tests.
**Verifica**: test que envía la misma clave dos veces y obtiene un solo recurso y la misma respuesta; `smoke-api.mjs` sin regresión.

**10. Auditoría de cambios.**
**Concepto**: trazabilidad; quién cambió qué y cuándo, sin guardar datos personales.
**Cambia**: interceptor EF en `Infrastructure/Persistence`, migración, tests.
**Verifica**: test que verifica el registro de una actualización y comprueba que no hay PII en el log de auditoría.

### Fase 4 — Seguridad y autorización (pasos 11-13)

**11. Permisos memorizados por request.**
**Concepto**: caché con alcance correcto; hoy cada comprobación golpea Identity, y el alcance permitido es *solo* dentro del request.
**Cambia**: `IAccessPolicy` en `Features/Auth`, tests.
**Verifica**: test que cuenta consultas de permisos en una request con varias comprobaciones; cambiar un permiso sigue aplicándose en el request siguiente.

**12. Rate limiting por usuario y por tipo de endpoint.**
**Concepto**: protección de recursos y fairness; hoy solo hay una política fija para `/auth`.
**Cambia**: configuración del limiter, endpoints, tests.
**Verifica**: test que observa 429 con `Retry-After`; `smoke-api.mjs` sigue pasando.

**13. Cabeceras de seguridad y escaneo continuo.**
**Concepto**: superficie HTTP y cadena de dependencias como parte del diseño.
**Cambia**: middlewares, `Program.cs`, `ci.yml` (escaneo de dependencias y de secretos), `docs/`.
**Verifica**: cabeceras observables en una respuesta real; el job nuevo falla ante un secreto plantado en un fixture de prueba y luego se retira.

### Fase 5 — Observabilidad y operación (pasos 14-17)

**14. Métricas de negocio reales.**
**Concepto**: métricas contra logs; cardinalidad y etiquetas acotadas.
**Cambia**: un `Meter` por feature (`TemplateApp.Tasks`, `TemplateApp.Auth`), instrumentación en las slices, tests.
**Verifica**: test con `MeterListener` que observa el contador/histograma emitido; `smoke-api.mjs --compose` con export OTLP no vacío.

**15. Correlación log↔traza automatizada.**
**Concepto**: verificación de señales en CI, no a ojo.
**Cambia**: scripts de `verify-template`, job `compose` de `ci.yml`, `artifacts/`.
**Verifica**: el job falla si un log de request no lleva trace/span o si el collector recibe métricas vacías.

**16. Health por dependencia.**
**Concepto**: disponibilidad accionable; readiness que refleje cada dependencia real.
**Cambia**: feature `Health`, tests.
**Verifica**: readiness 503 con base caída y con migraciones pendientes; live sigue 200 sin base.

**17. SLOs y runbooks.**
**Concepto**: operación por objetivos y presupuesto de error.
**Cambia**: documentos de operación y runbooks en `docs/`, alertas sobre las métricas ya existentes.
**Verifica**: cada SLO con su métrica consultada realmente en el backend de métricas; cada alerta con su runbook y una ejecución de prueba.

### Fase 6 — Datos y rendimiento (pasos 18-20)

**18. Índices medidos, no supuestos.**
**Concepto**: plan de consulta verificado.
**Cambia**: `Persistence/TaskItemConfiguration.cs`, migración, test de integración con `EXPLAIN`.
**Verifica**: test que afirma que la consulta de listado usa el índice esperado.

**19. Migraciones expand/contract ensayadas.**
**Concepto**: despliegue sin corte; nunca destructivo en el mismo release.
**Cambia**: migraciones, `docs/conventions.md`, evidencia de la secuencia completa.
**Verifica**: ciclo probado sobre una base con datos: agregar, backfill, cambiar código, borrar; readiness en verde en cada etapa.

**20. Lectura dedicada por módulo (CQRS liviano).**
**Concepto**: separar lectura de escritura sin tocar invariantes.
**Cambia**: contratos de lectura y operación en `Tasks`, tests.
**Verifica**: la lectura nueva devuelve exactamente el mismo resultado observable que la anterior; test comparativo antes de borrar la vieja.

### Fase 7 — Modularidad y límites (pasos 21-23)

**21. Contrato público por feature, y gate que lo haga cumplir.**
**Concepto**: módulos con interface chica.
**Nota**: el paso 0.5 ya hace cumplir "solo `Contracts`" entre features. Este paso queda para lo que falte, por ejemplo impedir que una slice sea pública.
**Cambia**: `dune/Dune.Analyzers`, `check-structure.mjs` + su test, `docs/conventions.md`.
**Verifica**: fixture negativa que referencia el interior de otra feature y falla con el diagnóstico esperado; fixture válida que compila.

**22. Un `DbContext` por feature.**
**Concepto**: propiedad de datos; el contexto dios impide extraer módulos.
**Cambia**: `Infrastructure/Persistence` hacia `Features/*/Persistence`, migraciones por contexto, arranque, tests.
**Verifica**: suite completa con dos contextos; smoke con base limpia y migrada; readiness en verde. **Paso de alto riesgo**: la migración de esquema es irreversible en producción, ensayala primero en el paso 19.

**23. Extraer un servicio con gateway adelante.**
**Concepto**: límite de despliegue y contrato remoto; el primer proceso propio.
**Cambia**: gateway (YARP), `compose.yaml`, Dockerfiles, `ci.yml`.
**Verifica**: `docker compose up --build --wait` + `smoke-api.mjs --compose` contra el nuevo despliegue; navegador completo; health de cada proceso.

### Fase 8 — Asincronía, mensajería y consistencia (pasos 24-28)

**24. Eventos de dominio + outbox transaccional, sin broker.**
**Concepto**: atomicidad entre estado y evento; el error clásico es publicar antes de confirmar.
**Cambia**: `Features/Tasks` (eventos), tabla de outbox, `Infrastructure/Persistence`, migración, tests.
**Verifica**: test donde el relay falla y el evento queda pendiente sin perder el cambio de estado; duplicado entregado dos veces y consumido una sola vez.

**25. Broker real con consumidor idempotente y DLQ.**
**Concepto**: at-least-once, deduplicación, mensajes venenosos, orden por partición.
**Cambia**: `compose.yaml` (broker), adaptador de mensajería, relay del outbox, health de la dependencia, tests de integración con el broker contenedorizado. ⏸ **DECISIÓN DE TOBI (pendiente, bloquea el paso)**: librería — Wolverine (MIT) o MassTransit v8 (Apache-2.0) si no hay presupuesto; v9 y NServiceBus son comerciales. MediatR NO es candidato aquí (es in-process, no bus).
**Verifica**: test de reintento y de DLQ; test que mata al consumidor a mitad y el mensaje se reprocesa sin efecto duplicado.

**26. Saga coreografiada (3 pasos con compensación).**
**Concepto**: transacción distribuida sin locks; compensación explícita.
**Cambia**: los dos o tres módulos participantes, mensajería, tests.
**Verifica**: test donde el último paso falla y la compensación deja el sistema consistente y observable.

**27. Saga orquestada con máquina de estados persistida.**
**Concepto**: orquestación, visibilidad del proceso, reanudación tras caída.
**Cambia**: módulo orquestador o infraestructura de sagas, tests. ⏸ **DECISIÓN DE TOBI (pendiente, bloquea el paso)**: tecnología — saga orquestada a mano sobre el bus elegido en el paso 25, o state machine del propio bus (MassTransit state machine vs Wolverine saga).
**Verifica**: test que reinicia el proceso a mitad de saga y esta continúa desde su estado persistido.

**28. Durable execution para un proceso largo con auditoría.**
**Concepto**: cuándo conviene una plataforma de workflow en lugar de una saga casera.
**Cambia**: worker nuevo, `compose.yaml`, tests de reanudación. ⏸ **DECISIÓN DE TOBI (pendiente, bloquea el paso)**: plataforma — workflow durable (p. ej. Temporal, Elsa) vs saga persistida propia del paso 27.
**Verifica**: proceso de varios días simulado con timers acelerados; historial consultable y auditable.

### Fase 9 — Integraciones externas (pasos 29-32)

**29. Primer cliente HTTP saliente con resiliencia.**
**Concepto**: timeouts en cascada, retry con backoff, circuit breaker, bulkhead; contra un servidor falso.
**Cambia**: el cliente en la feature que lo usa, la política de resiliencia en `Infrastructure/`, `Microsoft.Extensions.Http.Resilience`, tests con WireMock.
**Verifica**: test con fallo transitorio que termina en éxito, y con fallo persistente que abre el circuito; prueba de que el timeout interno es menor que el externo.

**30. Pasarela de pago detrás de un puerto propio.**
**Concepto**: ports & adapters, anti-corruption layer, webhook como fuente de verdad, PCI por checkout hospedado.
**Cambia**: feature nueva con Domain/Contracts/Persistence/Tests, tabla de eventos de webhook, `smoke-api.mjs` con caso de webhook firmado. ⏸ **DECISIÓN DE TOBI (pendiente, bloquea el paso)**: proveedor (p. ej. Stripe vs MercadoPago) y modalidad (checkout hospedado vs propio, con implicancia PCI).
**Verifica**: test de firma inválida rechazada, de replay de webhook sin efecto, y de orden de eventos alterado; el modelo propio no contiene tipos del proveedor.

**31. Máquina de estados de pago + conciliación programada.**
**Concepto**: dinero con estados auditados, idempotencia, conciliación diaria, jobs con visibilidad.
**Cambia**: feature de pagos, scheduler (⏸ **DECISIÓN DE TOBI pendiente**: Quartz.NET si no hay presupuesto, Hangfire si querés dashboard), tests.
**Verifica**: test de transición inválida rechazada; job de conciliación que detecta una diferencia plantada en un fixture.

**32. Integración por archivo (SFTP o CSV) con cuarentena.**
**Concepto**: integraciones no HTTP; idempotencia por checksum y control de rechazados.
**Cambia**: worker o feature de intercambio, tests con archivos de fixture.
**Verifica**: archivo con una fila inválida entra en cuarentena y el resto se procesa; reprocesar el mismo archivo no duplica.

### Fase 10 — Escala, cadena de suministro y gobernanza (pasos 33-36)

**33. Caché distribuida con invalidación medida.**
**Concepto**: caching con alcance y evidencia; hit ratio como métrica de ingeniería.
**Cambia**: `Microsoft.Extensions.Caching.Hybrid` en una lectura caliente, caché en `compose.yaml`, health de la caché, tests. ⏸ **DECISIÓN DE TOBI (pendiente, bloquea el paso)**: servidor — Redis (AGPL) o Valkey (BSD-3).
**Verifica**: test de invalidación tras escritura; test de aislamiento entre usuarios; métrica de hit ratio observable en el export OTLP. Revisar licencia del servidor elegido.

**34. Cadena de suministro.**
**Concepto**: qué se entrega y cómo se prueba que no fue alterado.
**Cambia**: `Dockerfile`, `ci.yml` (escaneo de imagen y dependencias, SBOM CycloneDX por release, procedencia), versionado centralizado de paquetes y lockfile, `docs/`.
**Verifica**: SBOM generado y descargable como artifact; el job falla ante una dependencia vulnerable plantada a propósito en un fixture.

**35. Despliegue progresivo con rollback probado.**
**Concepto**: liberar sin miedo; feature flags para desacoplar release de deploy.
**Cambia**: pipeline de entrega, configuración de canary o blue-green, feature flags, `docs/`.
**Verifica**: despliegue parcial con métricas comparadas y rollback ejecutado de verdad, con evidencia de ambas direcciones.

**36. Gobernanza final.**
**Concepto**: sostenibilidad organizacional, no técnica.
**Cambia**: `docs/adr/` con las decisiones de las fases nuevas, `docs/decisions.tsv`, matriz de licencias aprobada (Polly, MediatR, FluentAssertions, MassTransit si entraron), política de deprecación de API, simulacro de recuperación con RTO/RPO medidos.
**Verifica**: simulacro de restauración ejecutado con tiempos medidos y registrados en `artifacts/`; cada ADR con su alternativa descartada y su consecuencia.

---

## 5. Qué se aprende en cada tramo

| Tramo | Se aprende a manejar | Complejidad |
|---|---|---|
| Fases 1-2 | Verificación reproducible, contratos, configuración, errores, paginación | Baja |
| Fases 3-4 | Correctitud de escritura bajo concurrencia y reintentos, seguridad de superficie | Baja-media |
| Fases 5-6 | Operar con evidencia: métricas, SLOs, índices, migraciones sin corte | Media |
| Fases 7-8 | Límites reales entre módulos y procesos; consistencia eventual con outbox y sagas | Alta |
| Fases 9-10 | Integrar terceros con resiliencia y dinero de por medio; entregar y gobernar a escala | Alta |

**Cuándo se puede parar**: un producto de un equipo se sostiene bien al terminar la fase 6. La fase 7 paga cuando hay varios equipos. Las fases 8-10 pagan cuando hay procesos largos, dinero, terceros o auditoría.

**Dónde está el riesgo real**: el paso 22 (migración de esquema) y el 23 (extracción de proceso) son los únicos difíciles de revertir. Los pasos 25, 27 y 33 obligan a decidir por licencia antes de escribir una línea: Wolverine y Rebus son MIT, MassTransit v8 es Apache-2.0, MassTransit v9 y NServiceBus son comerciales, Redis es AGPL y Valkey BSD-3.

---

## 6. Verificación de este plan

Leí el estado real antes de escribirlo: `backend/Program.cs`, `Infrastructure/DependencyInjection/BackendInfrastructure.cs` (composición, ProblemDetails, Identity, JWT, rate limiter), `Infrastructure/Observability/ObservabilityExtensions.cs`, `Infrastructure/Persistence` (contexto, inicializador, migración única), `Features/Tasks/*` (`TasksModule`, `TasksEndpoints`, casos de uso), `Shared/Authorization/*` (`ResourceAuthorizer`, `PermissionService`), `Shared/Http/ApiResults.cs`, `backend/Tests/TemplateApp.Tests.csproj` y `Support/`, `dune/` (analizadores, gates y sus tests), `.github/workflows/ci.yml`, y la skill del proyecto `verify-template`. Los huecos listados en §1 salen de búsquedas sobre el backend: sin `BackgroundService`, sin caché (`IDistributedCache`/`HybridCache`), sin `DomainEvent`, sin tokens de concurrencia propios, sin `AddHttpClient`, sin idempotencia.

No modifiqué archivos, no compilé y no ejecuté gates para este plan. Los comandos de §3 son los declarados por el repo; su estado real de verde o rojo es justamente el producto del paso 1.
