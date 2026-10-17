# Informe: módulos deep/shallow y mediators/buses en .NET

> **Nota 2026-10-16**: investigación conservada como referencia. [ADR-0004](../adr/0004-vertical-slices-deep-modules.md) reemplaza sus recomendaciones de diseño: ya no rige "ya está aplicado (no tocar)" ni "no partir `TasksModule`". Cada slice pasa a ser una clase propia. Las citas a OpenSpec y las líneas de código citadas pueden estar desactualizadas.

**Alcance**: solo lectura. No modifiqué archivos, no compilé, no ejecuté gates. Leí el código y la documentación de este repo, las skills locales y fuentes web primarias. Al final está la lista exacta de lo verificado y lo no verificado.

---

## Parte 1 — Deep vs shallow modules

### 1.1 Corrección de premisa: la atribución del par deep/shallow

El par **deep/shallow modules** no es de Jimmy Bogard. Sus fuentes:

- **Bogard** acuñó **Vertical Slice Architecture** (2018) y su idea central es otra: *"Minimize coupling between slices, and maximize coupling in a slice"* ([jimmybogard.com/vertical-slice-architecture](https://www.jimmybogard.com/vertical-slice-architecture/)). Su ataque es a la indirección por capas: *"most abstractions melt away, and we don't need any kind of 'shared' layer abstractions like repositories, services, controllers"*. No usa el par deep/shallow en ese texto, y una búsqueda en su sitio no devuelve ningún artículo con ese vocabulario.
- **Deep vs shallow modules** es de **John Ousterhout**, *A Philosophy of Software Design* (2018): módulo *deep* = interface simple detrás de mucha implementación; *shallow* = interface casi tan compleja como la implementación ([extracto oficial Stanford](https://web.stanford.edu/~ouster/cgi-bin/aposd2ndEdExtract.pdf)).
- Dónde se cruzan: el marketing de MediatR (la librería de Bogard) vende el mediador como *"perfect fit for CQRS and Vertical Slice Architecture"* ([mediatr.io](https://mediatr.io/)). VSA es el estilo; MediatR es una de sus implementaciones típicas.

Conclusión: el patrón que pedís es de Ousterhout, y **este repo ya tiene su propia versión**, con una diferencia importante que explico abajo.

### 1.2 El vocabulario que usa este repo (skill local)

Hay una skill local sobre el tema: **`skill://codebase-design`**. La cito textualmente porque define el marco que uso abajo:

- **Module**: "anything with an interface and an implementation. Deliberately scale-agnostic: a function, class, package, or tier-spanning slice."
- **Interface**: "everything a caller must know to use the module correctly: the type signature, but also invariants, ordering constraints, error modes, required configuration".
- **Depth**: "leverage at the interface. The amount of behaviour a caller (or test) can exercise per unit of interface they have to learn."
- **Seam** (Feathers) y **Adapter**: "One adapter means a hypothetical seam. Two adapters means a real one."
- **Deletion test**: "If complexity vanishes, it was a pass-through. If complexity reappears across N callers, it was earning its keep."
- Y rechaza explícitamente la definición de Ousterhout: *"Depth as ratio of implementation-lines to interface-lines (Ousterhout): rewards padding the implementation. We use depth-as-leverage instead."*

Traducción práctica: **la profundidad se mide por lo que el llamador aprende, no por líneas de código**. Un módulo no es "deep" porque tenga 500 líneas adentro, sino porque el llamador puede hacer mucho con poco que aprender.

### 1.3 ¿Este repo tiene módulos deep o shallow?

Respuesta corta: **ambas cosas, y en el lugar correcto en cada caso**. Los módulos de aplicación son deep; los adapters son shallow a propósito.

| Módulo | Interface (lo que el llamador aprende) | Qué esconde | Veredicto |
|---|---|---|---|
| `TasksModule` (`TasksModule.cs:5-26`) | 5 métodos: `CreateAsync`, `ListAsync`, `GetByIdAsync`, `UpdateAsync`, `DeleteAsync`; firma `(callerId, request, ct) → ErrorOr<T>` | Validación FluentValidation, permiso `Tasks.Create`, owner-or-permission, consultas EF, paginación, `TimeProvider`, mapeo a `TaskResponse` | **Deep** |
| `ResourceAuthorizer` (`Shared/Authorization/ResourceAuthorizer.cs:12-23`) | 1 método: `CanAccessAsync(callerId, resource, permission, ct)` | Toda la política owner-or-permission; el orden 404-antes-de-403 vive en el llamador | **Deep** |
| `ApiResults` (`Shared/Http/ApiResults.cs:7-40`) | 1 método: `ToResult(result, success)` | Agrupa errores de validación a `ValidationProblem`; mapea `NotFound/Forbidden/Unauthorized/Conflict/Unexpected` a status + ProblemDetails | **Deep**, con 14 call sites (Auth 4, Tasks 5, Users 5) |
| `TasksEndpoints` (`TasksEndpoints.cs:11-19`) | Las 5 rutas + `ClaimsPrincipal` | 5 adapters de ~3-6 líneas que solo traducen HTTP → módulo (p. ej. `CreateTask.cs:66-67`) | **Shallow por diseño** (adapter HTTP) |
| `TaskErrors` (`TaskErrors.cs:1-10`) | 1 método `NotFound(Guid)` | Una constante `Task.NotFound` | Shallow trivial, pero concentra el catálogo de códigos |
| `Contracts/TaskResponse.cs`, `TaskListResponse.cs` | El tipo en sí | Nada | Shallow por construcción (DTO) |
| `TaskItemConfiguration.cs` | Contrato `IEntityTypeConfiguration` | Mapeo EF | Adapter exigido por EF |

Aplico el **deletion test** a los dos extremos, que es la prueba real:

- **Borrar `ApiResults`**: reaparece un `switch` de 5 ramas × 14 call sites. Era profundo, se queda.
- **Borrar `TasksModule`**: reaparece validación, autorización y EF en 5 endpoints, y el gate `DUNE0002` (EF prohibido dentro del tipo endpoint) empieza a fallar. Era profundo, se queda.
- **Borrar `TaskErrors`**: reaparece un literal `"Task.NotFound"` en cada operación. Barato hoy, útil para consistencia del catálogo de errores. Mantener.
- **Borrar `TasksEndpoints`**: la complejidad no desaparece, se muda al módulo, que entonces tendría que conocer `HttpContext` y `ClaimsPrincipal`. Es un adapter, su delgadez es el punto.

Una tensión honesta que conviene nombrar: según la regla de la skill, **`IPermissionService` tiene un solo adapter** (`PermissionService`, registrado en `BackendInfrastructure.cs:32`), o sea un *seam hipotético*. Se justifica por otra vía: `ResourceAuthorizer` es el consumidor real, y Dune prohíbe por analyzer que los endpoints toquen `UserManager` o EF (`openspec/specs/dune/spec.md`, requirement "Análisis semántico"). Es un seam defendido por el motor de reglas, no por polimorfismo. [INFERENCE] Si mañana no hubiera ningún otro consumidor, la skill pediría colapsarlo.

### 1.4 Cómo aplicar el patrón *concretamente* en este código

Cinco movimientos, ordenados por relación beneficio/riesgo. Cada uno con su evidencia.

**1) Ya está aplicado (no tocar).** El repo usa "un archivo por caso de uso con parciales de módulo y endpoints" — `docs/conventions.md`: *"Tarea es el patrón de casos de uso colocados. Un archivo por operación contiene las declaraciones parciales de `TasksModule` y `TasksEndpoints`… No agregues un módulo ni registro por archivo."* Esto es VSA literal (coupling vertical dentro del slice) sin abandonar un módulo deep por feature. Es la combinación que la skill premia: interface chica (5 métodos), implementación profunda repartida en archivos.

**2) Oportunidad de deepening real #1 — la regla del título está duplicada.**
- `CreateTask.cs:22-23`: `.Must(t => t.Trim().Length is >= 1 and <= 200).WithMessage("Title must be between 1 and 200 characters.")`
- `UpdateTask.cs:22-23`: **el mismo par de líneas, carácter por carácter.**

Dos call sites con la misma política. Hoy cambiar el máximo exige editar 2 validadores, 2 mensajes y sus tests. Deletion test: extraer un validador de título propio de la feature (un módulo interno de `Tasks`) y borrar las copias hace *desaparecer* complejidad en vez de repartirla. Es el candidato más limpio del repo.

**3) Oportunidad de deepening real #2 — la traducción validación→ErrorOr está triplicada.**
- `CreateTask.cs:35-37`
- `UpdateTask.cs:35-37`
- `UsersModule.cs:51-53`

Las tres hacen `validation.Errors.Select(f => Error.Validation(code: f.PropertyName, description: f.ErrorMessage)).ToList()`. Es la misma política "campo inválido ⇒ `Error.Validation` con el nombre de propiedad como código". Candidato: helper privado (seam **interno**, que según la skill no se expone en la interface). Nota: si tocás las tres, se puede compartir entre features solo porque es política HTTP pura; cualquier cosa que toque Domain de otra feature está prohibido por Dune.

**4) Profundizar sin agregar interface — el camino correcto aquí.** La skill dice: *"Depth is a property of the interface, not the implementation"* y *"internal seams vs external seams… Don't expose internal seams through the interface just because tests use them."* Es decir: los movimientos 2 y 3 se hacen **adentro** de `TasksModule`/`UsersModule`, sin agregar interfaces públicas, sin agregar registros DI y sin cambiar la firma que ven los endpoints. La profundidad sube; la superficie que el mantenedor aprende, no.

**5) Lo que NO hay que hacer — no envolver el módulo.** Sería shallow por construcción:
- No crear `ITasksService` con un solo adapter. Un adapter = seam hipotético (skill).
- No meter un `IMediator` entre `TasksEndpoints` y `TasksModule`: cambia un salto directo por uno dinámico y obliga al mantenedor a aprender la convención del bus para completar una operación HTTP. El propio ADR lo rechaza: *"un interface que no obliga al mantenedor a aprender generación dinámica para completar una operación HTTP"* (`docs/adr/0003-full-stack-template-dune.md`, "Alternativas y síntesis").
- No partir `TasksModule` en 5 módulos. `docs/conventions.md` lo prohíbe expresamente.

**6) Frontend, mismo criterio.** `tasks.service.ts` es deep en miniatura: 4 métodos, y detrás quedan `HttpParams`, `firstValueFrom`, la ruta `/api/v1/tasks` y el `responseType: 'text'` del DELETE. `tasks-models.ts` es DTO puro (shallow por construcción). El componente `tasks/tasks.ts` (338 líneas) es donde vive la profundidad de UI: signals, paginación, edición inline, modal de confirmación. Intacto.

---

## Parte 2 — Mediators y buses en .NET

### 2.1 Panorama y licencias (el dato que cambió el tablero)

Aquí está el giro de 2025-2026, y es relevante incluso aunque no adoptes nada.

| Librería | Autor / licencia vigente | Naturaleza | Costo real hoy |
|---|---|---|---|
| **MediatR** | Lucky Penny Software (Jimmy Bogard). v13+ dual: RPL-1.5 FOSS **o** comercial. v12 y anteriores: Apache-2.0/MIT, siguen gratis | Mediador in-process (`IRequest`/`IHandler`, pipelines) | Community gratis si ingresos < $5M USD, capital externo < $10M, no gobierno/universidad. Standard $799/año (1-10 devs), Professional $1499/año (11-50), Enterprise $6399/año. La vía FOSS es **RPL-1.5, copyleft recíproco** |
| **AutoMapper** | Lucky Penny. v15+ comercial | Mapeo | Mismo esquema que MediatR |
| **Wolverine** | JasperFx (Jeremy Miller). **MIT** | Mediador + bus: transporte (RabbitMQ/Kafka/Azure SB), outbox/inbox, sagas, codegen dinámico | Gratis; se vende soporte |
| **MassTransit** | Massient, Inc. v9 comercial. v8 Apache-2.0 sigue libre | Bus out-of-process: consumidores, transporte, sagas, retry | Licencia por key; v8 gratis sin soporte nuevo |
| **Mediator** (martinothamar) | **MIT** (verifiqué el LICENSE) | Mediador in-process con **source generators**, API similar a MediatR | Gratis. Alternativa libre si querés uniformidad de handlers |
| Brighter | — | Command processor / bus | **No verificado en esta investigación** |

Fuentes: [mediatr.io](https://mediatr.io/) (precios + FAQ: *"Two packages: AutoMapper (15.0.0+) and MediatR (13.0.0+)… Earlier versions remain governed by their original open-source licenses"*), [NuGet MediatR 13.0.0 license](https://www.nuget.org/packages/MediatR/13.0.0/License), [LICENSE.md de Lucky Penny](https://github.com/LuckyPennySoftware/MediatR/blob/main/LICENSE.md) (RPL-1.5 salvo licencia comercial), [LICENSE de Wolverine = MIT](https://github.com/JasperFx/wolverine/blob/main/LICENSE), [JasperFx: "Marten 8 / Wolverine 4 will be released under the MIT OSS license"](https://jeremydmiller.com/2025/04/02/a-quick-note-about-jasperfxs-plans-for-marten-wolverine), [docs de licencia de MassTransit](https://masstransit.massient.com/configuration/license) + [massient.com/license](https://massient.com/license), [LICENSE de Mediator](https://raw.githubusercontent.com/martinothamar/Mediator/main/LICENSE).

Detalle fino que importa: la FAQ de Lucky Penny dice que la key **no bloquea en runtime**: *"Enforcement is through warning/error log messages - there are no artificial runtime limits"*. O sea: adoptar MediatR v13 sin licencia no rompe el build, te deja un problema legal/contractual, no técnico. Para un template que se copia a otras empresas, eso es peor: el incumplimiento viaja con la copia.

### 2.2 Pros y contras, para aplicaciones grandes

**Mediador in-process (MediatR, Mediator, Wolverine en modo mediator)**

- **A favor**: uniformidad total (un handler por request, un pipeline para validación/logging/transacción); decoradores reutilizables; una convención única que escala a decenas de equipos; el endpoint se desacopla del handler.
- **En contra**: el flujo deja de ser visible para el compilador (reflexión en MediatR, source-gen en Mediator/Wolverine); el orden de los `IPipelineBehavior` es invisible en el call site; el debugger y el stack trace agregaron un salto; y en MediatR v13 hay costo de licencia (o copyleft RPL).
- **Se paga a cambio de algo**: la ganancia aparece con N requests × M comportamientos transversales. Con 5 requests y 2 comportamientos, la indirección se paga sin retorno.

**Bus out-of-process (MassTransit, Wolverine con transporte)**

- **A favor**: eso que no se puede improvisar — entrega garantizada, retries con backoff, outbox transaccional, inbox/idempotencia, sagas, dead-letter, mensajería entre servicios, escalado por consumidores.
- **En contra**: exige infraestructura real (broker), observabilidad del broker, y comprensión de semántica de entrega. Sin un segundo proceso que consuma, todo eso es costo sin beneficio observable: es la definición de dependencia decorativa.

### 2.3 Recomendación para ESTE proyecto: **seguir con módulos directos**

**Conclusión**: no adoptar mediator ni bus. La arquitectura actual es correcta para el tamaño y las restricciones del repo. Cinco razones con evidencia local.

1. **No hay consumidor externo ni segundo proceso.** El sistema es un API + web + PostgreSQL + collector OTLP (`docs/architecture.md`, sección "Frontend y operación"). El propio ADR lo dice al rechazar la alternativa A: *"conserva el bus y el almacenamiento de mensajes sin un consumidor externo real"* (`docs/adr/0003-full-stack-template-dune.md`). El valor del bus (entrega entre procesos) es inobservable aquí.

2. **Los gates ya lo prohíben por escrito.** `dune/check-structure.mjs:123-136` tiene `checkNoWolverine()`, que falla ante `\bWolverine(Fx)?\b`, `\bJasperFx\b` o `\bIMessageBus\b` en el producto; se invoca en `:231` y tiene su caso negativo en `dune/check-structure.test.mjs:98-108`. `docs/conventions.md`: *"No hay handlers de bus, controllers parciales, wrappers genéricos de repositorio"*. Adoptar un bus exige modificar contrato OpenSpec + gate + test, no solo un `PackageReference`.

3. **El sistema de verificación no sabría medir la ganancia.** `docs/conventions.md`: *"Pruebas de feature cruzan HTTP con WebApplicationFactory y PostgreSQL real… No usa EF InMemory ni mocks que solo repiten respuestas."* El bus se verifica con brokers reales, replays y outbox; nada de eso existe en el modelo de pruebas actual, y agregarlo contradice la regla de no simular.

4. **Costo de licencia en un artefacto que se distribuye como template.** MediatR v13 + AutoMapper 15 + MassTransit v9 son comerciales, con la vía libre en RPL-1.5 (copyleft). Un template que se copia a una empresa con ingresos > $5M le transfiere al consumidor la obligación de licenciar o de abrir su fuente. Eso es un defecto de entrega, no una preferencia de estilo. Wolverine y Mediator sí son MIT.

5. **Evidencia local de que el diseño sin bus puntuó mejor.** El ADR registra la comparación: candidato A (conservaba Wolverine, controllers parciales, transacciones automáticas) obtuvo **12/30**; candidato B (módulos directos, transacciones explícitas) obtuvo **20/30** en una revisión independiente. *"Lo elegimos por locality y por una interface que no obliga al mantenedor a aprender generación dinámica"*.

**Refuerzo por tamaño real**: 4 features (Auth, Users, Tasks, Health), 5 rutas en Tasks, un solo contenedor de API. La duplicación cross-cutting que un pipeline automatizaría son **3 líneas** repetidas en 3 archivos (`CreateTask.cs:35-37`, `UpdateTask.cs:35-37`, `UsersModule.cs:51-53`). Profundizar eso con un helper interno cuesta mucho menos que un bus y no agrega superficie.

### 2.4 Cuándo revisar esta decisión (disparadores concretos, no "algún día")

| Disparador observable | Qué adoptar | Por qué ese y no otro |
|---|---|---|
| Aparece un segundo proceso que consume eventos (worker, jobs, integración) | **Wolverine** (MIT) con transporte + outbox | Único que da bus con licencia libre y outbox; el mediador in-process viene incluido, no hace falta sumar dos dependencias |
| Se necesita bus pero el presupuesto es cero y el equipo ya usa MassTransit | **MassTransit v8** (Apache-2.0) | v9 es comercial; v8 sigue libre, sin soporte nuevo |
| >3 features × >5 casos de uso con la misma secuencia transversa (validar → autorizar → persistir → responder), y el equipo pide una convención única | **Mediator** (MIT, source-gen) o `IPipelineBehavior` propio de 30 líneas | Evita licencia y evita codegen dinámico; el pipeline propio mantiene el flujo visible para el compilador |
| Se necesita MediatR por razones ya existentes en el equipo | MediatR v13 con licencia o quedarse en **v12** (Apache/MIT) | v12 sigue gratis y bajo licencia permisiva; v13 sin licencia implica RPL-1.5 |

La regla de decisión, en una línea: **mediador in-process se justifica por N×M alto y convención única; bus out-of-process se justifica por límites de proceso reales.** Ni una ni otra condición se cumple hoy.

### 2.5 Adoptar un bus aquí costaría, además

- `openspec/specs/dune/spec.md` (requirement "Estructura y pruebas colocadas": *"MUST rechazar dependencias legacy"*).
- `dune/check-structure.mjs:123-136` y su test negativo.
- `docs/conventions.md` y `docs/architecture.md:9` (*"No hay mediator, bus, transacciones Wolverine…"*), más un ADR nuevo que superseda parcialmente ADR-0003.
- El límite de profiling: `compose.yaml` sumaría un broker y el collector ya cubre señales; no hay lugar donde hoy se observe el beneficio.

---

## Verificación y límites

**Leído en el repo (hechos observados)**: `backend/Features/Tasks/*.cs`, `backend/Shared/Authorization/{ResourceAuthorizer,PermissionService}.cs`, `backend/Shared/Http/ApiResults.cs`, `backend/Infrastructure/DependencyInjection/BackendInfrastructure.cs:22-38`, `backend/Tests/TemplateApp.Tests.csproj`, `frontend/src/app/features/tasks/*`, `dune/check-structure.mjs:123-136,231`, `dune/check-structure.test.mjs:98-108`, `dune/Dune.props`, `backend/TemplateApp.csproj`, `docs/architecture.md`, `docs/conventions.md`, `docs/adr/0003-full-stack-template-dune.md`, `openspec/specs/dune/spec.md`, `CONTEXT.md`, `README.md`. Greps con línea exacta: regla de título (CreateTask.cs:22-23 = UpdateTask.cs:22-23), traducción validación→ErrorOr (CreateTask.cs:35-37, UpdateTask.cs:35-37, UsersModule.cs:51-53), 14 call sites de `ApiResults.ToResult`, cero dependencias de bus en `TemplateApp.csproj`.

**Leído como skill local**: `skill://codebase-design` y `skill://codebase-design/DEEPENING.md`. También revisé `skill://c-feature-split` y la descarté: trata límites de feature, no profundidad de módulo.

**Fuentes web**: Bogard VSA (texto completo), mediatr.io (precios + FAQ), LICENSE de Mediator y de Wolverine, docs de licencia de MassTransit / massient.com, nota de JasperFx sobre Wolverine 4/Marten 8 MIT, extracto de Ousterhout.

**No verificado / fuera de alcance**: licencia vigente de Brighter; si CI remoto corrió alguna vez (no lo ejecuté); cualquier medición de performance de mediadores vs llamadas directas (no hice benchmarks). No compilé, no corrí tests, no modifiqué archivos. Los juicios marcados [INFERENCE] son razonamiento sobre lo leído, no hechos observados.
