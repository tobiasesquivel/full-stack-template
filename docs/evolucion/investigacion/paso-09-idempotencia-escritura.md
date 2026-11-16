# Paso 9: idempotencia de escritura (investigación)

- Fecha: 2026-11-16.
- Alcance: el paso 9 del [plan](../plan.md) (`plan.md:304-307`). Un cliente puede repetir un `POST /api/v1/tasks` sin crear una segunda tarea: manda una clave, y si la clave ya se usó, el servidor devuelve la respuesta de la primera vez. El paso suma un header de pedido y respuestas nuevas, así que cambia el contrato HTTP y rige la regla 3 de `AGENTS.md` (`AGENTS.md:37`): backend, frontend y tests juntos.
- Decisiones que ya están tomadas y que este informe no reabre: EF Core 10 con `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (`backend/TemplateApp.csproj:35`), PostgreSQL 18, ErrorOr 2.1.1 con catálogo de errores y DUNE0014 (`docs/conventions.md:76`), `ApiResults` como único mapeo de `Error` a HTTP (`docs/conventions.md:74`), el `title` con la descripción del error (`docs/decisions.tsv:124`), la sesión BFF con antiforgery (`docs/architecture.md:61-66`), la versión `xmin` del paso 8 (`docs/decisions.tsv:144-148`) y la forma de ADR-0004.
- Método: solo fuentes primarias. Son el borrador de la IETF `draft-ietf-httpapi-idempotency-key-header`, [RFC 9110](https://www.rfc-editor.org/rfc/rfc9110.html), la documentación de Stripe, [Google AIP-155](https://google.aip.dev/155), las [Azure REST API Guidelines](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md) con [OASIS Repeatable Requests](https://docs.oasis-open.org/odata/repeatable-requests/v1.0/repeatable-requests-v1.0.html), Microsoft Learn (ASP.NET Core y EF Core), la documentación de PostgreSQL 18, angular.dev, la especificación de Web Crypto y la de Playwright.
- Evidencia propia: un experimento descartable fuera del repo.
  - "Experimento": una app ASP.NET Core 10 de un solo archivo en el directorio temporal del sistema, con `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 y `ErrorOr` 2.1.1 (SDK 10.0.401), contra un contenedor `postgres:18` descartable. Tiene un `POST /tasks` con un endpoint filter de idempotencia, una tabla de tareas y una tabla de claves. Un cliente HTTP en el mismo proceso corre 11 casos (T0 a T10) y el log de EF muestra cada SQL. Lo corrí dos veces con el mismo resultado. Después borré el contenedor.
  - No se tocó código del repo.

## Glosario corto

- **Idempotente**: repetir el pedido deja el servidor igual que hacerlo una vez. RFC 9110 lo define así: "the intended effect on the server of multiple identical requests with that method is the same as the effect for a single such request" ([RFC 9110 §9.2.2](https://www.rfc-editor.org/rfc/rfc9110.html#name-idempotent-methods)). `PUT` y `DELETE` lo son; `POST` no.
- **Clave de idempotencia** (idempotency key): un valor único que genera el cliente para una intención ("crear la tarea Comprar pan"). Si repite el pedido, repite la clave.
- **Huella** (fingerprint): un hash del pedido que se guarda junto a la clave. Sirve para detectar que alguien usó la misma clave con otro cuerpo.
- **Repetición** (replay): devolver la respuesta guardada de la primera vez, sin volver a ejecutar la operación.
- **Endpoint filter**: código que ASP.NET Core corre antes y después del handler de un endpoint de Minimal APIs. Ve los argumentos ya ligados y el resultado.

## Conclusión

- **El problema es real.** `CreateTask` crea una tarea nueva en cada pedido (`backend/Features/Tasks/CreateTask/CreateTask.cs:34-44`). Si la respuesta del primer `POST` se pierde en la red y el usuario vuelve a pulsar "Crear", hay dos tareas iguales. Hoy el frontend no reintenta solo (no hay `retry` en `frontend/src/app/`), pero el usuario sí: el título queda en el formulario después de un error (`frontend/src/app/features/tasks/tasks/tasks.ts:92-103`).
- **Contrato:** recomiendo el header `Idempotency-Key` del borrador de la IETF, que es lo que usan Stripe y otros siete servicios que lista el borrador. El borrador está vivo: versión -08 del 2026-10-22, documento del grupo `httpapi`, todavía no es RFC.
- **Obligatoria u opcional:** recomiendo opcional en el servidor y siempre presente en el frontend. Es lo que hacen Stripe, Google (AIP-155) y Azure (OASIS). Sin clave, el `POST` funciona como hoy. Es decisión de Tobi.
- **Alcance y huella:** la clave vale por usuario (`OwnerId` + `Key` como clave primaria). La huella es SHA-256 de método, ruta y el request ya ligado (`CreateTaskRequest`) serializado con las opciones JSON de la app. El experimento mostró que así un JSON con otro espaciado o un campo de más da la misma huella.
- **Qué se guarda:** solo la respuesta 2xx, y en la **misma transacción** que crea la tarea. Entonces la clave existe si y solo si la tarea existe. Un 400, un 403 o una excepción hacen rollback de las dos cosas, y el cliente puede reintentar con la misma clave. Se guardan status, `Location`, `Content-Type` y los bytes del cuerpo. La repetición suma `Idempotent-Replayed: true`, como Stripe.
- **Concurrencia:** `INSERT ... ON CONFLICT DO NOTHING` sobre la clave primaria, dentro de la transacción. PostgreSQL hace **esperar** al segundo pedido con la misma clave hasta que el primero confirma; después el segundo ve la fila y repite la respuesta. El experimento lo confirmó: dos pedidos simultáneos, una sola tarea, el mismo cuerpo. Si se prefiere el 409 que sugiere el borrador, `SET LOCAL lock_timeout` lo da (también confirmado).
- **Caída del proceso:** PostgreSQL hace rollback de toda transacción abierta cuando se corta la conexión. No queda ninguna clave "pendiente" trabada. El experimento cortó la conexión que tenía la clave y el pedido que esperaba siguió y creó la tarea.
- **Dónde vive:** un `IEndpointFilter` genérico en `backend/Infrastructure/Http/`, que la slice activa con una línea en `CreateTaskEndpoint`. La operación `CreateTask` no cambia: su `SaveChangesAsync` entra en la transacción del filtro porque comparten el `AppDbContext` del request. ASP.NET Core 10 no trae idempotencia propia; las librerías de terceros guardan en caché, fuera de la transacción.
- **Vencimiento:** recomiendo publicar "al menos 24 h" (como Stripe), guardar `CreatedAt` y no borrar filas en este paso, porque el repo no tiene trabajos programados (`plan.md:33`). La limpieza va como pendiente, junto con la de `Sessions` (`progreso.md:111`).
- **Errores nuevos:** clave con otro cuerpo da **422** (`Idempotency.KeyReused`), como pide el borrador. ErrorOr no tiene un tipo para 422 y hoy `ApiResults` mandaría un `Error.Custom(422, …)` a 500 (`backend/Shared/Http/ApiResults.cs:25-33`). Hay que sumar ese mapeo.
- **Rojo real:** el test del plan (misma clave dos veces) falla hoy: el header se ignora y salen dos `id` distintos. También fallan hoy el de clave reusada con otro título (201 en vez de 422) y el de dos pedidos simultáneos (dos tareas).
- **Regla de Dune:** el concepto del plan es una regla ("a todo POST de negocio…"), y la regla 4 pide hacerla ejecutable. Recomiendo un paso 9.1 con DUNE0016, como se hizo con 5.4 y 6.2. "POST de negocio" no está definido; recomiendo excluir Auth.

## 1. Contrato en el cable: qué header y qué estados

**Respuesta:** `Idempotency-Key` con un UUID, 422 para clave reusada con otro cuerpo, y 400 para una clave mal formada.

**El borrador de la IETF**

- Estado hoy: versión -08, del 2026-10-22, "WG Document" del grupo httpapi, Standards Track, vence el 2027-04-25. El IESG no lo tomó todavía ([Datatracker](https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/)). No es un RFC: el nombre del header y los estados pueden cambiar.
- Sintaxis: el valor "MUST be a String (Section 3.3.3 of [RFC9651])", o sea un string de Structured Fields, con comillas (§2.1). Unicidad: "MUST be unique and MUST NOT be reused with another request with a different request payload". Formato: "It is RECOMMENDED that a UUID [RFC9562] or a similar random identifier be used" (§2.2) ([draft-08](https://www.ietf.org/archive/id/draft-ietf-httpapi-idempotency-key-header-08.html)).
- Huella: "An idempotency fingerprint MAY be used in conjunction with an idempotency key" y "is generated from request payload data by the resource" (§2.4, misma fuente).
- Vencimiento: "The resource MAY require time based idempotency keys to be able to purge or delete a key upon its expiry" y "SHOULD define such expiration policy and publish it in the documentation" (§2.3, misma fuente).
- Repetición: si el pedido original terminó, el recurso responde de forma consistente con el pedido completado. Si sigue en curso, "SHOULD respond with a resource conflict error" (§2.6, misma fuente).
- Errores (§2.7, misma fuente), los tres como Problem Details:
  - Falta la clave en una operación que la exige: **400**, título de ejemplo "Idempotency-Key is missing".
  - Clave reusada con otro cuerpo: **422**, "Idempotency-Key is already used".
  - Pedido con la misma clave todavía en curso: **409**, "A request is outstanding for this Idempotency-Key".
- Seguridad (§5, misma fuente): "Establish a fixed format for the idempotency key and publish the key's specification", "Always validate the key as per its published specification before processing any request", y usar una clave de búsqueda compuesta con atributos del cliente que solo conoce el servidor.
- El borrador no dice nada sobre guardar o no las respuestas de error, ni sobre un header que marque la repetición (misma fuente, leído completo).
- 422 en HTTP: "the server understands the content type of the request content [...] and the syntax of the request content is correct, but it was unable to process the contained instructions" ([RFC 9110 §15.5.21](https://www.rfc-editor.org/rfc/rfc9110.html#name-422-unprocessable-content)).

**Lo que hace la industria**

| Quién | Cómo viaja | Repetición | Clave con otro cuerpo | Simultáneo | Retención |
|---|---|---|---|---|---|
| Stripe | Header `Idempotency-Key`, hasta 255 caracteres, sugiere UUID v4 ([Stripe, Idempotent requests](https://docs.stripe.com/api/idempotent_requests)) | Guarda "the status code and body of the first request [...] including `500` errors"; marca con `Idempotent-Replayed: true` ([Stripe, Advanced error handling](https://docs.stripe.com/error-low-level)) | "errors if they're not the same" ([Idempotent requests](https://docs.stripe.com/api/idempotent_requests)) | 409 si "another request with the same key is still executing" ([Advanced error handling](https://docs.stripe.com/error-low-level)) | "at least 24 hours" (misma fuente) |
| Google | Campo `request_id` en el cuerpo del pedido, UUID, "should be optional" ([AIP-155](https://google.aip.dev/155)) | "should return the response for the previously successful request"; puede devolver el estado actual si el recurso cambió (misma fuente) | No lo define | No lo define | "any reasonable timeframe" |
| Azure | Headers `Repeatability-Request-ID` y `Repeatability-First-Sent` de OASIS, "YOU SHOULD" en `POST` ([Azure Guidelines](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md)) | "the same response code and body", o re-ejecutar si el original dio 4xx o 5xx ([OASIS §5](https://docs.oasis-open.org/odata/repeatable-requests/v1.0/repeatable-requests-v1.0.html)) | "MAY return an error 400" (misma fuente) | No lo define | "at least 5 minutes" ([Azure Guidelines](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md)) |

- Azure además prefiere evitar el problema: "use PUT or PATCH to create a resource" ([Azure Guidelines](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md)). Google permite que el cliente elija el id del recurso al crear, y un duplicado da `ALREADY_EXISTS` ([AIP-133](https://google.aip.dev/133)). Las dos son "idempotencia natural": el cliente manda `PUT /tasks/{id}` con un id que generó él.
- El repo ya nombra la opción de Stripe: "clave de idempotencia al enviar (`Idempotency-Key`, Stripe la mantiene 24 h)" (`docs/evolucion/informe-dotnet-empresarial.md:285`), y la pone como respuesta al antipatrón "Retry sin idempotencia" (`informe-dotnet-empresarial.md:441`, `:451`).

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. `Idempotency-Key` (borrador IETF, Stripe) | El nombre que va camino a estándar y que ya usan muchos servicios; el plan habla de "clave"; encaja con `POST`. | Un header nuevo que el documento OpenAPI no muestra solo (sección 8). El borrador puede cambiar antes de ser RFC. |
| B. `Repeatability-*` (OASIS, Azure) | Estándar publicado (OASIS 1.0). | Dos headers, uno con fecha (`Repeatability-First-Sent`) que el cliente tiene que guardar; menos difundido fuera de Azure. |
| C. `requestId` en el cuerpo (Google) | No hay header nuevo; se ve en OpenAPI como campo del request. | Mezcla transporte con datos; cada request de negocio suma el campo. |
| D. `PUT /tasks/{id}` con id del cliente | Idempotencia natural de HTTP, sin tabla de claves. | Cambia la forma de crear (`POST` sale), el cliente genera el `Guid` de la tarea y no hay mecanismo genérico; el plan pide uno en `Infrastructure/` (`plan.md:306`). |

Recomendación: **A**. Es lo que el plan describe ("la misma clave", `plan.md:307`), lo que nombra el informe del repo y lo que hace Stripe.

**Formato de la clave.** El borrador pide un formato fijo y validarlo (§5). Recomiendo aceptar solo un UUID en forma `D` (36 caracteres, `Guid.TryParseExact(key, "D", out _)`), porque el frontend lo genera con `crypto.randomUUID()` (sección 9). Una clave que no es UUID da 400 `Idempotency.KeyInvalid`. Stripe acepta cualquier texto hasta 255; la ventaja del formato fijo es que una clave de baja entropía (por ejemplo `"1"`) no entra. Como la clave vale por usuario (sección 3), adivinar una clave ajena no da nada. [INFERENCIA]

**Sobre el valor como String de Structured Fields.** El borrador -08 pide el valor entre comillas (`Idempotency-Key: "8e03978e-..."`). Stripe lo muestra sin comillas (`Idempotency-Key: KG5LxwFBepaKHyUD`, [Stripe](https://docs.stripe.com/api/idempotent_requests)). Recomiendo aceptar las dos formas: quitar un par de comillas si lo hay, y validar el UUID. Es un detalle que conviene fijar en un test. [INFERENCIA]

## 2. ¿La clave es obligatoria u opcional en `POST /api/v1/tasks`?

**Respuesta:** recomiendo opcional, con el frontend mandándola siempre.

- El borrador solo pide 400 si la operación está "documented as requiring the header" (§2.7, [draft-08](https://www.ietf.org/archive/id/draft-ietf-httpapi-idempotency-key-header-08.html)): las dos formas son válidas.
- Stripe: "all `POST` requests accept idempotency keys", no la exige ([Stripe](https://docs.stripe.com/api/idempotent_requests)). Google: "request IDs should be optional" ([AIP-155](https://google.aip.dev/155)).

| Opción | Contrato | Tests y smoke | Frontend y e2e |
|---|---|---|---|
| Opcional | Sin clave, igual que hoy (201 nuevo cada vez). Con clave, repetición. Cambio compatible en v1. | Nada que cambiar en los 8 `POST` directos de los tests ni en `TestAccounts.CreateTaskAsync` (`backend/Tests/Support/TestAccounts.cs:114-116`). Solo se suman tests nuevos. El smoke suma un bloque. | `TasksApi.create` manda la clave. El e2e de crear afirma el header. |
| Obligatoria | Sin clave, 400 `Idempotency.KeyMissing`. Cambio incompatible en v1, como en los pasos 7 y 8 (`docs/decisions.tsv:139`, `:146`). | Cambian `CreateTaskAsync`, `CreateTaskTests.cs:22,43,47`, `IssueCsrfTokenTests.cs:119`, `ErrorContractTests.cs:26` y los 6 `POST /tasks` del smoke (`smoke-api.mjs:177-190`), que hoy no puede mandar headers propios (`smoke-api.mjs:19-34`). | Igual que la opcional. |

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| Opcional | Lo que hace la industria. Un cliente nuevo (un script, la futura app móvil de `architecture.md:68`) funciona sin saber nada. Menos archivos tocados: un concepto por paso. | Un cliente que no manda la clave no queda protegido. La garantía depende de que el frontend la mande, y eso lo prueban el e2e y Vitest. |
| Obligatoria | Ningún `POST /tasks` puede duplicar por reintento. Igual que el paso 8, donde una versión ausente era el defecto. | Rompe v1. Ruido en todos los tests y en el smoke. Un 400 de clave ausente compite con el 400 de validación: hay que decidir cuál sale primero. |

Recomendación: **opcional**. En el paso 8, aceptar la versión ausente dejaba el defecto en el `PUT`. Acá, la clave ausente deja el comportamiento de hoy, que es el que define HTTP para `POST` ([RFC 9110 §9.2.2](https://www.rfc-editor.org/rfc/rfc9110.html#name-idempotent-methods)). Es decisión de Tobi.

## 3. Alcance de la clave y qué entra en la huella

**Alcance: por usuario.**

- El borrador recomienda "a composite cache lookup key", que combine la clave del cliente con atributos del cliente que solo conoce el servidor (§5, [draft-08](https://www.ietf.org/archive/id/draft-ietf-httpapi-idempotency-key-header-08.html)). Stripe lo pide único "within your account" ([Stripe, Advanced error handling](https://docs.stripe.com/error-low-level)).
- En el repo, el usuario es el claim `ClaimTypes.NameIdentifier` que escribe `SessionPrincipal.Create` (`backend/Features/Auth/SessionPrincipal.cs:29`). La clave primaria de la tabla es `(OwnerId, Key)`. Así, la misma clave de Alice y de Bob son dos filas, y Bob nunca recibe la respuesta guardada de Alice. El experimento lo probó (T2c: Bob con la clave de Alice creó su propia tarea).
- **Cómo lee el filtro el usuario.** Las slices usan `user.GetCallerId()` (`backend/Features/Auth/Contracts/ClaimsPrincipalExtensions.cs:7-15`). Pero el filtro vive en `Infrastructure/`, y "`Infrastructure/` y `Shared/` no nombran ninguna feature" (`docs/conventions.md:22`). Recomiendo que el filtro lea el claim estándar de ASP.NET Core `ClaimTypes.NameIdentifier` directamente, y que corte con una excepción si falta (el grupo de tareas exige sesión: `backend/Features/Tasks/TasksFeature.cs:17`). Es la misma regla que `GetCallerId`, repetida en un lugar más. DUNE0003 permitiría usar `Auth.Contracts` desde `Infrastructure` (`dune/Dune.Analyzers/InfrastructureBoundaryAnalyzer.cs:82-92`), pero conventions.md no. Lo marco en la sección 12.
- **Endpoint en la huella, no en la clave primaria.** Si la misma clave llega a otro endpoint, es un error del cliente: la huella incluye método y ruta, entonces da 422. [INFERENCIA, a partir de la regla del borrador: una clave no se reusa con otro pedido]

**Huella: SHA-256 del pedido ya ligado.**

| Opción | Cómo | Qué ganás | Qué perdés |
|---|---|---|---|
| A. Cuerpo crudo | Hash de los bytes del body | Exacto: nada se pierde. | Hay que leer el body dos veces: `EnableBuffering` en un middleware antes del binding. Un JSON con otro espaciado da otra huella y un 422 falso. |
| B. Request ligado | Hash de `JsonSerializer.SerializeToUtf8Bytes(request, opcionesDeLaApp)` | Canónico por construcción: el orden de las propiedades sale del record, el espaciado y los campos desconocidos no cuentan. El filtro ya tiene el argumento (`EndpointFilterInvocationContext.Arguments`, [Learn, filters](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/min-api-filters?view=aspnetcore-10.0)). | Un campo que el binding ignora no cuenta para la huella. Para este request, es lo correcto. |

- Experimento T2b: la misma clave con `{ "title" :   "Comprar pan" , "extra": 1 }` dio repetición, no 422, con la opción B.
- La huella suma `"{método} {ruta}\n"` antes del JSON. En el experimento usé `Request.Path`. Con varias versiones de la API, `/api/v1/tasks` y `/api/v2/tasks` darían huellas distintas, que es lo esperable. [INFERENCIA]
- El filtro sabe qué argumento es el request porque la slice se lo dice con el tipo: `.WithIdempotency<CreateTaskRequest>()`, y el filtro toma `context.Arguments.OfType<TRequest>().Single()`. Así no hay reflexión sobre el handler.
- Hash: `SHA256.HashData` de la BCL ([Learn](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.sha256.hashdata)). Se guarda como `bytea` (32 bytes).

Recomendación: **B**.

## 4. Qué se guarda y qué se repite

**Respuesta:** solo la respuesta 2xx que se confirmó junto con la tarea: status, `Location`, `Content-Type` y los bytes del cuerpo.

**Errores: guardar o no**

- Stripe guarda todo lo que pasa "after endpoint execution begins", también 400 y 500; no guarda si "incoming parameters fail validation" o hay un pedido simultáneo ([Stripe](https://docs.stripe.com/api/idempotent_requests), [Advanced error handling](https://docs.stripe.com/error-low-level)).
- OASIS permite re-ejecutar si el original dio 4xx o 5xx ([OASIS §5](https://docs.oasis-open.org/odata/repeatable-requests/v1.0/repeatable-requests-v1.0.html)). El borrador no lo dice (sección 1).

| Opción | Qué ve el cliente | Qué ganás | Qué perdés |
|---|---|---|---|
| A. Solo 2xx, en la misma transacción | Un 400, 403 o 500 no deja rastro: el reintento con la misma clave se ejecuta de nuevo. | Una sola invariante: la clave existe si y solo si la tarea existe. El rollback de un error borra las dos. Cero código para "pendiente" o "falló". | El reintento de un error no devuelve el mismo error: se ejecuta de nuevo. Un 403 repetido se vuelve a evaluar, y puede dar 201 si entretanto le dieron el permiso. |
| B. También errores (Stripe) | El reintento de un 500 repite el 500. | El cliente sabe que el resultado no cambió. | Para guardar un error hay que confirmar la fila de la clave **sin** la tarea: dos transacciones, una fila "pendiente" que puede quedar trabada si el proceso cae, y un timeout para liberarla. |

- Experimento T5: el handler lanzó una excepción después de insertar la clave. Resultado: 500, cero claves y cero tareas guardadas; el reintento con la misma clave dio 201. T5b: un 400 de validación hizo rollback (`Rolled back transaction` en el log) y no guardó la clave.

Recomendación: **A**. Encaja con la regla del repo: "Una operación con varias escrituras atómicas abre una transacción antes de escribir y confirma al terminar" (`docs/conventions.md:84`).

**Qué bytes se guardan**

- El filtro ejecuta el `IResult` del handler sobre un `MemoryStream`, lee status, `Location`, `Content-Type` y el cuerpo, los guarda, confirma, y recién después escribe los bytes en la respuesta real. Si el commit falla, el cliente no vio el 201. Experimento T1: la segunda respuesta tuvo el mismo cuerpo byte a byte y el mismo `Location`.
- Alternativa: leer el resultado por interfaces. `Results.Created(...)` devuelve `Created<T>`, que implementa `IStatusCodeHttpResult` e `IValueHttpResult` y tiene una propiedad `Location` (experimento T9). Pero no hay una interfaz común para `Location`; habría que usar reflexión o atar el filtro a `Created<T>`. El buffer es genérico y repite exactamente lo que salió.
- Otros headers (`api-supported-versions` del versionado, `Set-Cookie`) no se guardan: los vuelve a poner el pipeline en cada pedido. [INFERENCIA: no lo probé con Asp.Versioning]
- **Lo que se repite es la foto de la primera vez.** Si después alguien renombró la tarea, la repetición devuelve el título y la `version` (el `xmin` del paso 8) de la creación. Google lo permite al revés: "may return the current state of the resource instead" ([AIP-155](https://google.aip.dev/155)). En este repo no importa: después de crear, `Tasks` recarga la lista (`tasks.ts:96-98`).
- También se repite el `createdAt` con 7 decimales que hoy sale del objeto en memoria (pendiente ya anotado en `progreso.md`, "El `createdAt` de la respuesta 201…"). La repetición es igual a la primera respuesta, no a la fila.

**Cómo se marca la repetición**

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| `Idempotent-Replayed: true` (Stripe, [Advanced error handling](https://docs.stripe.com/error-low-level)) | El test y el smoke ven que la segunda respuesta salió de la tabla y no de la slice. Sirve para enseñar el concepto. | Un header que ningún estándar define; el cliente no lo necesita. |
| Nada | La repetición es idéntica a la original, como pide el borrador (§2.6). | El test solo puede comparar ids y contar tareas. |

Recomendación: **marcarla**. El costo es una línea y el header no cambia el cuerpo.

## 5. Concurrencia y atomicidad con PostgreSQL y EF Core

**Respuesta:** una transacción que envuelve la slice, con `INSERT ... ON CONFLICT DO NOTHING` sobre la clave primaria como primer paso. PostgreSQL serializa los pedidos con la misma clave.

**Cómo funciona**

1. El filtro abre la transacción en el `AppDbContext` del request: `await using var tx = await db.Database.BeginTransactionAsync(ct)`.
2. Inserta la clave con `ExecuteSqlAsync` interpolado. EF Core no tiene upsert propio (el issue [dotnet/efcore#4526](https://github.com/dotnet/efcore/issues/4526) "Merge/Upsert/AddOrUpdate support" sigue abierto en Backlog). La forma interpolada convierte cada valor en parámetro ([EF Core, SQL queries](https://learn.microsoft.com/en-us/ef/core/querying/sql-queries)), que es la que permite `BannedSymbols.txt` (`dune/BannedSymbols.txt:19-20`).
3. `ON CONFLICT DO NOTHING` "simply avoids inserting a row as its alternative action" y el conteo del comando es la cantidad de filas insertadas ([PostgreSQL, INSERT](https://www.postgresql.org/docs/current/sql-insert.html)). `ExecuteSqlAsync` devuelve 1 si la clave es nueva y 0 si ya estaba (experimento T1: "afectó 1 fila" y después "afectó 0 filas").
4. Con 1: llama a `next`. `CreateTask` hace `SaveChangesAsync` (`CreateTask.cs:44`) dentro de la misma transacción, porque el contexto es el mismo. EF lo documenta: varias `SaveChanges` y consultas entran en la transacción abierta con `BeginTransaction`, y EF "automatically creates a savepoint before saving any data" ([EF Core, Transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions)). El log del experimento muestra `Created transaction savepoint.` antes del `INSERT INTO "TaskItems"`.
5. Si el resultado es 2xx: `UPDATE` de la fila de la clave con la respuesta, commit y después los bytes al cliente. Si no: rollback.
6. Con 0: lee la fila. Huella distinta: 422. Huella igual: repetición.

**¿Es el mismo `AppDbContext`?** Sí. `AddDbContext` lo registra con vida "por request" (`backend/Infrastructure/DependencyInjection/BackendInfrastructure.cs:25-26`), `CreateTask` es scoped (`TasksFeature.cs:7`) y el filtro lo pide a `HttpContext.RequestServices`. [INFERENCIA por el código; en el experimento el handler recibía el contexto por parámetro, del mismo scope]

**Dos pedidos a la vez con la misma clave**

- PostgreSQL: "If a conflicting row has been inserted by an as-yet-uncommitted transaction, the would-be inserter must wait to see if that transaction commits" ([PostgreSQL, Index Uniqueness Checks](https://www.postgresql.org/docs/current/index-unique-checks.html)). La página de `INSERT` lo confirma: las tablas con índices únicos "might block if concurrent sessions perform actions that lock or modify rows matching the unique index values" ([INSERT](https://www.postgresql.org/docs/current/sql-insert.html)).
- Experimento T3: el pedido A tardó 1500 ms en el handler. El pedido B, 200 ms después, quedó esperando en su `INSERT ... ON CONFLICT` (el log mide 1284 ms para ese comando), vio la fila confirmada, dio 201 con `Idempotent-Replayed: true` y el mismo `id`. Una sola tarea.
- Esto **no** es lo que sugiere el borrador, que pide 409 para un pedido en curso (§2.7). Se consigue con un tope de espera: `SET LOCAL lock_timeout = '…'` al abrir la transacción. `lock_timeout` hace "Abort any statement that waits longer than the specified amount of time while attempting to acquire a lock" ([PostgreSQL, Client Connection Defaults](https://www.postgresql.org/docs/current/runtime-config-client.html)), y `SET LOCAL` vale solo para la transacción actual ([PostgreSQL, SET](https://www.postgresql.org/docs/current/sql-set.html)). Experimento T4: con 300 ms, B recibió `55P03 canceling statement due to lock timeout` a los 306 ms y el filtro respondió 409; el reintento posterior dio la repetición.

| Opción | Qué ve el segundo pedido | Qué ganás | Qué perdés |
|---|---|---|---|
| Esperar (sin `lock_timeout`) | 201 con la misma tarea, cuando el primero termina | Cero código extra, ningún error nuevo; el test del plan ("un solo recurso y la misma respuesta") sale igual con pedidos simultáneos. | Una conexión de la base ocupada mientras espera. Se aparta del SHOULD del borrador. |
| 409 con `lock_timeout` | 409 `Idempotency.InProgress`, y el cliente reintenta | Lo que piden el borrador y Stripe. La espera tiene tope. | Un `SET LOCAL`, un `catch` de `PostgresException` 55P03, un código nuevo, y el cliente tiene que reintentar ante 409. |

Recomendación: **esperar**. Hoy el único cliente deshabilita "Crear" mientras envía (`frontend/src/app/features/tasks/tasks/tasks.html:21`), así que dos pedidos simultáneos con la misma clave son raros, y el usuario solo puede bloquear sus propias claves. El 409 queda como mejora si aparece un cliente que reintenta en paralelo.

**Alternativas que descarto**

- **Fila "pendiente" en una transacción aparte** (insertar y confirmar la clave, ejecutar, actualizar): permite el 409 y guardar errores, pero si el proceso cae entre los dos commits la clave queda trabada y hace falta un vencimiento del "pendiente". La transacción única no tiene ese estado.
- **Advisory lock** (`pg_advisory_xact_lock` con un hash de usuario y clave, [PostgreSQL, Advisory Locks](https://www.postgresql.org/docs/current/explicit-locking.html#ADVISORY-LOCKS)): el repo ya usa uno para migrar (`backend/Infrastructure/Persistence/DatabaseInitializer.cs:20`). Acá no hace falta: la espera del índice único ya es el lock, y además deja la fila.
- **`Add` + `SaveChanges` + atrapar la violación única**: funciona. Experimento T7: `DbUpdateException` con `PostgresException` interna, `SqlState=23505` (`PostgresErrorCodes.UniqueViolation`), `ConstraintName=PK_IdempotencyKeys`. Pero usa una excepción para un caso esperado, que el informe del repo nombra como antipatrón (`informe-dotnet-empresarial.md:450`). `ON CONFLICT` no lanza nada.

**Si el proceso cae a mitad del pedido**

- PostgreSQL: "For either normal or abnormal termination, any open transaction is rolled back, not committed" ([PostgreSQL, Message Flow §Termination](https://www.postgresql.org/docs/current/protocol-flow.html)).
- Experimento T6: otra conexión insertó la clave y no confirmó. El pedido HTTP con esa clave quedó esperando. Corté esa conexión con `pg_terminate_backend`. El pedido siguió a los 1013 ms, insertó la clave y creó la tarea. No quedó nada trabado.
- Lo que no probé: matar el proceso de la API (no la conexión). Al morir el proceso se cierra el socket y PostgreSQL ve el mismo corte. [INFERENCIA, a partir de la cita de arriba]

**Un límite del diseño: la slice no puede abrir otra transacción**

- Experimento T10: `BeginTransactionAsync` sobre un contexto que ya tiene una da `InvalidOperationException: The connection is already in a transaction and cannot participate in another transaction.`
- `CreateTask` no abre transacción. `RegisterUser` sí (`backend/Features/Auth/RegisterUser/RegisterUser.cs:45`). Entonces el filtro no se puede poner en `POST /auth/register` sin cambiar esa slice. Es otra razón para dejar Auth fuera (sección 11).

**La tabla**

- Experimento, DDL generado por EF:

  ```sql
  CREATE TABLE "IdempotencyKeys" (
      "OwnerId" character varying(450) NOT NULL,
      "Key" character varying(255) NOT NULL,
      "Fingerprint" bytea NOT NULL,
      "CreatedAt" timestamp with time zone NOT NULL,
      "StatusCode" integer,
      "Location" text,
      "ContentType" text,
      "Body" bytea,
      CONSTRAINT "PK_IdempotencyKeys" PRIMARY KEY ("OwnerId", "Key")
  );
  CREATE INDEX "IX_IdempotencyKeys_CreatedAt" ON "IdempotencyKeys" ("CreatedAt");
  ```

- En el repo: la entidad y su `IEntityTypeConfiguration` en `backend/Infrastructure/Persistence/`. `AppDbContext` aplica toda configuración del ensamblado (`backend/Infrastructure/Persistence/AppDbContext.cs:22`). `OwnerId` con largo 450 como `TaskItem.OwnerId` (`backend/Features/Tasks/Persistence/TaskItemConfiguration.cs:13-15`). Con UUID obligatorio, `Key` puede ser `uuid` en vez de texto. [INFERENCIA]
- Migración por CLI, sin editar: `dotnet ef migrations add AddIdempotencyKeys --project backend --output-dir Infrastructure/Persistence/Migrations` (`docs/conventions.md:87`). A diferencia del paso 8, esta sí trae DDL real.
- `TimeProvider` para `CreatedAt` (`docs/conventions.md:86`).

**Forma del filtro** (no es código final):

```csharp
public sealed class IdempotencyFilter<TRequest> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!IdempotencyKey.TryRead(context.HttpContext.Request, out Guid? key))
            return ApiResults.Problem(HttpErrors.IdempotencyKeyInvalid);
        if (key is null)
            return await next(context);

        byte[] fingerprint = Fingerprint(context, context.Arguments.OfType<TRequest>().Single());
        await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(ct);
        int inserted = await db.Database.ExecuteSqlAsync($"INSERT INTO \"IdempotencyKeys\" (...) VALUES (...) ON CONFLICT (\"OwnerId\", \"Key\") DO NOTHING", ct);
        if (inserted == 0)
            return await ReplayOrRejectAsync(owner, key.Value, fingerprint, ct);

        object? result = await next(context);
        return await StoreIfSuccessAsync(result, tx, ct);
    }
}
```

## 6. Dónde vive el mecanismo genérico

**Respuesta:** un endpoint filter en `backend/Infrastructure/Http/`, activado por la slice.

**¿ASP.NET Core 10 trae algo?** No encontré nada.

- La búsqueda de código de GitHub en `dotnet/aspnetcore` no da resultados para `Idempotency`; para `Idempotent` solo aparecen usos no relacionados (`SafeHttpMethods.cs`, Blazor, Kestrel). La búsqueda de issues no da ninguna propuesta de idempotencia de API (búsqueda con `gh search code` y `gh search issues`, 2026-11-16).
- La página de filtros de .NET 10 no menciona idempotencia ([Learn, filters](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/min-api-filters?view=aspnetcore-10.0)).
- Conclusión: el mecanismo es propio o de un paquete de terceros. [INFERENCIA: una búsqueda vacía no prueba la ausencia, pero ninguna fuente primaria la contradice]

**Las tres ubicaciones**

| Opción | Cuándo corre | Qué ganás | Qué perdés |
|---|---|---|---|
| A. Endpoint filter (`IEndpointFilter`) | Dentro del endpoint, después de los middlewares y del binding | Ve el request tipado y el `IResult`; corre después de autenticación, autorización y antiforgery; se activa por endpoint con una línea. | El header no aparece solo en OpenAPI. |
| B. Middleware | Antes del binding, para todo pedido | Ve el body crudo y la respuesta completa. | Tiene que buscar en la metadata del endpoint si aplica; lee el body antes que el binding (`EnableBuffering`); corre también para pedidos que el binding va a rechazar. |
| C. En la slice | Dentro de `CreateTask` | Todo explícito. | No es genérico: el plan pide el mecanismo en `Infrastructure/` (`plan.md:306`), y cada slice nueva lo copiaría. |

- Un filtro "Run code before and after the endpoint handler", "Inspect and modify parameters" e "Intercept the response behavior" ([Learn, filters](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/min-api-filters?view=aspnetcore-10.0)). Un filtro con `IEndpointFilter` "can resolve dependencies from DI", pero el filtro mismo no se resuelve de DI (misma fuente).
- Un filtro se puede poner en un grupo, y "Adding filters or metadata to a group results in the same behavior as adding them individually to each endpoint" ([Learn, route handlers](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/route-handlers?view=aspnetcore-10.0)). No lo recomiendo en el grupo `/tasks`: el grupo tiene también `PUT` y `DELETE` (`TasksFeature.cs:19-23`), y el filtro necesita el tipo del request.
- En el repo: `backend/Infrastructure/Http/IdempotencyFilter.cs` y una extensión `WithIdempotency<TRequest>(this RouteHandlerBuilder)` en el mismo lugar que `VersionedApi.cs`. `CreateTaskEndpoint.Map` pasa a `tasks.MapPost("", HandleAsync).WithIdempotency<CreateTaskRequest>()` (`backend/Features/Tasks/CreateTask/CreateTaskEndpoint.cs:9`). `CreateTask.cs` y `CreateTaskEndpoint.HandleAsync` no cambian.

**Librerías de terceros** (decisión de herramienta: es de Tobi, regla 5, `AGENTS.md:39`)

- [IdempotentAPI](https://github.com/ikyriak/IdempotentAPI): MIT, v2.6.0, tiene paquete para Minimal APIs (`IdempotentAPIEndpointFilter`), guarda en `IDistributedCache` o FusionCache, usa locks distribuidos opcionales (RedLock.net o DistributedLock, que soporta Postgres), vence a las 24 h por defecto, header por defecto `IdempotencyKey` (sin guion).
- Hay muchos paquetes chicos en NuGet (OrionOnce, IdemShield, DKNet.AspCore.Idempotency y otros), varios en versión 0.x. No los evalué uno por uno.

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| Propio (filtro de unas 100 líneas) | La clave y la tarea en la misma transacción de PostgreSQL; sin paquete nuevo; el concepto queda a la vista. | Código propio que mantener y probar. |
| IdempotentAPI | Probado por otros, configurable. | Guarda en caché, fuera de la transacción de la tarea: la invariante "clave si y solo si tarea" se pierde. Trae un paquete nuevo y, para varias instancias, una caché distribuida que el repo no tiene (caché es el paso 33, `docs/conventions.md:88`). Header con otro nombre por defecto. |

Recomendación: **propio**. Es un laboratorio, el concepto es el mecanismo, y la atomicidad con la tarea es lo que lo hace correcto.

## 7. Vencimiento y limpieza de las claves

- El borrador pide definir y publicar la política de vencimiento (§2.3). Stripe retiene "at least 24 hours" ([Stripe](https://docs.stripe.com/error-low-level)); Azure, "at least 5 minutes" ([Azure Guidelines](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md)); Google, "any reasonable timeframe" ([AIP-155](https://google.aip.dev/155)). IdempotentAPI usa 24 h por defecto ([IdempotentAPI](https://github.com/ikyriak/IdempotentAPI)).
- El repo no tiene trabajos programados: "Ningún trabajo programado" (`plan.md:33`). El primer paso con algo programado es el 31 (`plan.md:427`). Las filas de `Sessions` ya esperan una limpieza sin paso asignado (`progreso.md:111`).

| Opción | Qué hace | Qué ganás | Qué perdés |
|---|---|---|---|
| A. Publicar "al menos 24 h", guardar `CreatedAt`, no borrar en este paso | Las filas quedan; un pendiente une la limpieza de claves y de sesiones | Un concepto por paso (reglas 1 y 7). Con UUID no hay choques entre claves viejas. | La tabla crece una fila por tarea creada con clave, hasta que exista la limpieza. |
| B. Vencimiento al leer | `ON CONFLICT ... DO UPDATE ... WHERE "CreatedAt" < corte`: una clave vencida se trata como nueva | La política publicada se cumple en el contrato. | Más SQL y un test con `FakeTimeProvider`; igual no borra filas. |
| C. Borrado oportunista | `DELETE` de las claves vencidas del mismo usuario en cada `POST` con clave | Acota la tabla sin trabajo programado. | Una escritura más por pedido; los usuarios inactivos dejan filas. |
| D. `BackgroundService` periódico | Un hosted service con `PeriodicTimer` que borra las vencidas | La solución completa; serviría también para `Sessions`. | Es otro concepto (trabajo programado), y necesita su paso. |

Recomendación: **A**, con un pendiente "limpieza periódica de `IdempotencyKeys` y `Sessions`".

**Borrado de la cuenta.** La fila guarda el cuerpo de la respuesta, que lleva el título de la tarea. Hoy borrar un usuario borra en cascada su perfil y sus tareas (`docs/architecture.md:47`). Sin FK, sus claves quedan. Recomiendo una FK de `IdempotencyKeys.OwnerId` a `UserProfiles.AuthId` con cascada, declarada en `AppDbContext.ConfigureCrossFeatureRelationships` (`AppDbContext.cs:28-42`), como la de `TaskItems` (`docs/conventions.md:83`). Así no quedan títulos de un usuario borrado. El test `DeleteUserTests` ya prueba la cascada de tareas (`backend/Features/Users/DeleteUser/DeleteUserTests.cs:22`). [INFERENCIA: no lo probé]

## 8. Encaje con el resto del pipeline

**Orden de los middlewares** (`backend/Program.cs:31-37`): logging, exception handler, status code pages, rate limiter, autenticación, autorización, antiforgery. `WebApplicationBuilder` envuelve todo eso con `UseRouting` y `UseEndpoints`, y "Endpoints execute at the position of UseEndpoints" ([Learn, Routing](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/routing?view=aspnetcore-10.0)). El filtro es parte del endpoint, así que corre después de todos.

| Pieza | Qué pasa con un pedido con clave | ¿Se guarda la clave? |
|---|---|---|
| Rate limiter | Solo los tres endpoints de `/auth` tienen política (`backend/Features/Auth/AuthFeature.cs:76-84`, `RegisterUserEndpoint.cs:9`). `/tasks` no se limita. | No aplica |
| Sin sesión | `RequireAuthorization` en el grupo (`TasksFeature.cs:17`) responde 401 `Auth.SessionRequired` antes del endpoint | No: el filtro no corre |
| Sin antiforgery | `UseAuthAntiforgery` responde 400 `Auth.AntiforgeryFailed` antes del endpoint (`backend/Features/Auth/SessionHttpPolicy.cs:37-53`) | No |
| JSON roto, `Content-Type` no soportado | El binding responde 400 o 415. "Endpoint filters don't run for body-reading failures (such as malformed JSON or unsupported media types)" ([Learn, filters](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/min-api-filters?view=aspnetcore-10.0)). Experimento T8: JSON roto con clave, 400, cero claves. | No. `ErrorContractTests.cs:188` y `:199` no cambian. |
| Validación del título | `CreateTask` devuelve 400 `Validation.Failed` (`CreateTask.cs:27-29`) | No: rollback (experimento T5b) |
| Sin permiso `Tasks.Create` | 403 `Task.Forbidden` (`CreateTask.cs:31-32`) | No: rollback |
| Excepción | `GlobalExceptionHandler` da 500 `Http.Unexpected` (`backend/Infrastructure/Middlewares/GlobalExceptionHandler.cs:13`); la transacción del filtro se descarta antes | No (experimento T5) |

**Catálogo de errores**

- Los errores de idempotencia no son de ninguna feature. El catálogo transversal es `Shared/Http/HttpErrors.cs` (`docs/conventions.md:76`), que ya tiene un código sin prefijo `Http.` (`Validation.Failed`, `backend/Shared/Http/HttpErrors.cs:8-10`). DUNE0014 exige el prefijo de la clase solo dentro de `Features/` (`dune/Dune.Analyzers/ErrorCatalogAnalyzer.cs:49-52`).
- Propuesta: `Idempotency.KeyInvalid` (400, `Error.Validation` con código de campo, o `Error.Custom`), `Idempotency.KeyReused` (422) y, solo si se elige el 409, `Idempotency.InProgress` (`Error.Conflict`). Con clave obligatoria, `Idempotency.KeyMissing` (400).
- Para la clave inválida conviene un 400 con el código fijo y no un error de validación por campo: el header no es un campo del cuerpo, y `errors` lleva las claves del JSON (`docs/conventions.md:76`). [INFERENCIA]
- **422 no existe en ErrorOr.** `ErrorType` tiene Failure, Unexpected, Validation, Conflict, NotFound, Unauthorized y Forbidden. `Error.Custom(422, …)` da `NumericType = 422` (experimento T9), pero `ApiResults.StatusFor` hace `switch` sobre `error.Type` y todo lo desconocido sale como 500 (`ApiResults.cs:25-33`). Hay que sumar una rama: un `Custom` cuyo `NumericType` es un status HTTP válido sale con ese status. `conventions.md:74` lista los mapeos y tiene que sumar esa línea.

**Versionado y OpenAPI**

- La ruta versionada no cambia (`backend/Infrastructure/Http/VersionedApi.cs:23-26`). La huella incluye la ruta, así que una clave usada en v1 y en una futura v2 daría 422.
- OpenAPI: el filtro no agrega el header al documento, porque el handler no lo declara. Hoy el documento tampoco describe las respuestas de error (pendiente "OpenAPI no describe las respuestas de error" en `progreso.md`). Recomiendo sumarlo a ese pendiente y no resolverlo en este paso. [INFERENCIA: no generé el documento]

**Logs y datos personales**

- La clave no es un secreto, pero el cuerpo guardado lleva el título. Nada del filtro va al log salvo la clave y el resultado (`docs/conventions.md:96`). Stripe pide no usar datos personales como clave ([Stripe](https://docs.stripe.com/api/idempotent_requests)); con UUID no pasa.

## 9. Frontend (Angular 22)

**Hoy**

- `TasksApi.create(title)` hace `http.post('/api/v1/tasks', { title })` sin headers (`frontend/src/app/features/tasks/api/tasks-api.ts:20-22`).
- `Tasks.create()` limpia el formulario solo si sale bien; ante un error deja el título y muestra `createError` (`tasks.ts:92-103`). Un error de red dice "Sin conexión con el servidor. Revisa tu red y pulsa Reintentar." (`frontend/src/app/core/http-error.ts:28-30`). El usuario pulsa "Crear" otra vez: ese es el reintento real.
- Nadie reintenta solo. `provideHttpClient` solo tiene `authInterceptor` y la configuración de XSRF (`frontend/src/app/app.config.ts:17-20`), y el interceptor no reintenta (`frontend/src/app/features/auth/session/auth-interceptor.ts:7-21`). `HttpClient` no reintenta solo; angular.dev remite al operador `retry()` de RxJS ([angular.dev, Making requests](https://angular.dev/guide/http/making-requests)). Los reintentos automáticos entran recién en el paso 29 (`docs/conventions.md:88`): este paso no los agrega.

**Cuándo nace la clave: una por intención**

- Stripe: "When retrying the same operation, preserve the idempotency key and parameters", y "If you need to change the request parameters [...] Use a new key" ([Stripe, Advanced error handling](https://docs.stripe.com/error-low-level)).
- En `Tasks`: la intención es "crear una tarea con este título". `Tasks` guarda en memoria el último intento `{ title, key }`. Al enviar:
  - Si el título (ya recortado) es el mismo del intento anterior, reusa la clave.
  - Si cambió, o no hay intento, genera una clave nueva con `crypto.randomUUID()`.
  - Si el `POST` sale bien (201, sea nuevo o repetido), borra el intento.
- Así un reintento después de un error de red repite la clave, y un título corregido no choca con un 422.
- `crypto.randomUUID()` "generates a new version 4 UUID" y es `[SecureContext]` ([W3C Web Crypto](https://w3c.github.io/webcrypto/)). `localhost` y `https` son contextos seguros ([W3C Secure Contexts](https://w3c.github.io/webappsec-secure-contexts/)). La app ya necesita `https` fuera de `localhost`, porque `__Host-session` es `Secure` (`docs/conventions.md:94`).
- La clave vive en memoria del componente. Recargar la página la pierde. Guardarla en `sessionStorage` la mantendría, pero FRONT015 prohíbe el storage del navegador (`docs/conventions.md:94`). Stripe sugiere persistir la clave si el reintento cruza un reinicio ([Advanced error handling](https://docs.stripe.com/error-low-level)); acá no aplica, y lo anoto como límite.

**Quién pone el header**

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. `TasksApi.create(title, idempotencyKey)` con `headers: { 'Idempotency-Key': key }` ([angular.dev](https://angular.dev/guide/http/making-requests)) | Explícito: el que conoce la intención (`Tasks`) decide la clave. | Cada `POST` futuro repite la línea. |
| B. Un interceptor que agrega una clave a todo `POST` | Nadie se olvida. | El interceptor no conoce la intención: genera una clave por pedido, y un segundo clic manda otra. Solo protegería un `retry()` dentro del mismo observable, que la app no usa. |

Recomendación: **A**. La regla de la clave (mismo título, misma clave) es una función pura que se prueba en Vitest, por ejemplo `features/tasks/tasks/create-attempt.ts` con su `.spec.ts` (`docs/conventions.md:106`).

**Errores en la UI**

- Con la recomendación (esperar en vez de 409, y clave nueva si cambia el título), la UI no debería ver ni 409 ni 422 de idempotencia. Si igual llegan, `describeHttpError` diría "Ya existe un registro con esos datos." para 409 (`http-error.ts:46-47`) y el texto genérico de servidor para 422 (`http-error.ts:48-49`). No recomiendo textos nuevos en este paso. [INFERENCIA]

## 10. Tests, smoke, e2e y el rojo real

**Soporte de tests**

- `TestSession.SendAsync` no acepta headers propios (`backend/Tests/Support/TestSession.cs:26-35`). Hay que sumarle un parámetro opcional de headers en `Tests/Support`, no copiar un helper (`docs/conventions.md:102`).
- `SessionResponse` ya trae `Text`, `Location` y los headers (`backend/Tests/Support/SessionResponse.cs`): alcanza para comparar respuestas.

**Tests de la slice** (`CreateTaskTests.cs`, HTTP real y PostgreSQL real, `docs/conventions.md:101`)

1. **El del plan:** Alice manda `POST` con la clave K y "Comprar pan" dos veces. Afirma: los dos 201, el mismo `Text` byte a byte, el mismo `Location`, `Idempotent-Replayed: true` en el segundo, y una sola "Comprar pan" en la lista.
   - **Hoy da rojo:** el header se ignora. El segundo `id` es otro, entonces falla la igualdad del cuerpo, y la lista tiene dos. Lo mismo se vio en el experimento sin clave (T0: dos 201 con ids distintos, dos tareas).
2. **Clave reusada con otro título:** K con "Comprar pan" y después K con "Comprar leche". Afirma 422 `Idempotency.KeyReused` con `ProblemAssert` (`backend/Tests/Support/ProblemAssert.cs`). **Hoy:** 201, rojo.
3. **Dos pedidos a la vez:** dos `POST` con K lanzados con `Task.WhenAll` desde dos clientes con la misma cookie (`TestSession.CopyTo`, `TestSession.cs:110-120`). Afirma: los dos 201 con el mismo `id`, una sola tarea. **Hoy:** dos tareas, rojo. Es determinista: el que inserta primero la clave gana y el otro espera (experimento T3).
4. **Clave mal formada:** `Idempotency-Key: abc` da 400 `Idempotency.KeyInvalid`. **Hoy:** 201, rojo.
5. **Un error no guarda la clave:** K con título en blanco da 400, y el rollback se lleva la fila de la clave. Después, la misma K con "Comprar pan" da 201, no 422: no hay huella guardada con qué comparar. Es la invariante "clave si y solo si tarea". El experimento lo mostró con un 500 (T5: el reintento con la misma clave dio 201) y con un 400 (T5b: cero claves guardadas). **Hoy:** pasa (verde): es un test de protección, no de rojo.
6. **Alcance por usuario:** Bob con la clave K de Alice crea su propia tarea. **Hoy:** pasa (verde).
7. **Sin clave:** dos `POST` sin header crean dos tareas. **Hoy:** pasa. Documenta que la clave es opcional (si Tobi lo elige).

- La excepción a mitad de la slice (experimento T5) no tiene un gancho simple en el repo. Lo dejo probado en el experimento y no recomiendo un decorador de test para este paso.
- Dónde: los tests 1 a 7 usan `POST /tasks`, entonces van en `CreateTaskTests.cs`. Si se quiere separar el mecanismo, `backend/Tests/Idempotency/` (`docs/conventions.md:103`).

**Migración**

- La evidencia es el archivo generado por CLI, `dotnet ef migrations list` con la migración aplicada y el diff sin ediciones, como en el paso 8.

**Smoke (`.agents/skills/verify-template/scripts/smoke-api.mjs`)**

- `ApiClient.request` arma los headers adentro (`smoke-api.mjs:19-34`). Suma una opción `headers`.
- Bloque nuevo después de los `POST` de Alice (`smoke-api.mjs:180-190`): `POST` con una clave dos veces (mismo `id`, `idempotent-replayed: true`), y la misma clave con otro título (422 con `code: "Idempotency.KeyReused"`). Las aserciones existentes no cambian si la clave es opcional: "sin regresión", como pide el plan (`plan.md:307`).

**E2E (`frontend/e2e/tasks.spec.ts`)**

- El helper `create()` espera el 201 (`tasks.spec.ts:28-37`). Puede afirmar además que el pedido lleva `idempotency-key` con forma de UUID (`request.headers()`).
- E2E del concepto: el primer `POST` llega al servidor pero el navegador no ve la respuesta. Con Playwright, `route.fetch()` "Performs the request and fetches result without fulfilling it" y `route.abort()` corta con un código de error ([Playwright, Route](https://playwright.dev/docs/api/class-route)). La página muestra el error de red, el usuario pulsa "Crear" otra vez, y la lista tiene una sola tarea con ese título. La documentación no dice si `abort()` se puede llamar después de `fetch()`; sin comprobar.
- Las e2e corren una vez, al cerrar el paso, porque cambia el frontend (`AGENTS.md`, "Verificación").

## 11. ¿El paso introduce una regla? Dune

**Respuesta:** sí. El concepto del plan dice "a todo POST de negocio se le puede repetir la intención" (`plan.md:305`). Es una regla sobre todo el código, y la regla 4 pide hacerla ejecutable con fixture válida y violación deliberada (`AGENTS.md:38`).

**Qué es un "POST de negocio"**

| `POST` de hoy | ¿Lleva clave? | Por qué |
|---|---|---|
| `/tasks` (`CreateTaskEndpoint.cs:9`) | Sí | Crea un recurso; es el caso del paso. |
| `/auth/register` (`RegisterUserEndpoint.cs:9`) | No, recomiendo | Ya es idempotente por naturaleza: repetirlo da 409 `Auth.UserNameAlreadyExists` (`smoke-api.mjs:155-158`). Además abre su propia transacción (sección 5). |
| `/auth/login` (`OpenSessionEndpoint.cs:8`) | No | Repetirlo abre otra sesión; no crea un recurso de negocio. |
| `/auth/logout` (`CloseSessionEndpoint.cs:6`) | No | Cerrar dos veces deja lo mismo. |

**Cómo lo comprobaría Dune**

- DUNE0016 (analyzer Roslyn): en un `*Endpoint.cs` de `backend/Features/<F>/` con `F` distinto de `Auth`, toda invocación `MapPost(...)` tiene que encadenar `.WithIdempotency<T>()`. Fixture válida: `MapPost("", H).WithIdempotency<R>()`. Violación: `MapPost("", H)` solo. Las rutas de escritura ya se mapean solo en `*Endpoint.cs` (DUNE0011, `docs/conventions.md:95`), así que el analyzer sabe dónde mirar.
- Alternativa en ejecución: un test que recorre `EndpointDataSource` y exige una metadata de idempotencia en todo `POST` fuera de `/auth`, como `EveryApiEndpoint_HasVersionMetadata` (`backend/Tests/Versioning/ApiVersioningTests.cs:55-74`). Es más simple, pero no es Dune.

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. Paso 9.1 aparte con DUNE0016 | Un cambio observable por paso, como 5.3 → 5.4 y 6 → 6.2 (`docs/decisions.tsv:122`, `:133`). | El paso 9 cierra con la regla escrita en conventions.md pero sin hacer cumplir hasta 9.1. |
| B. DUNE0016 dentro del paso 9 | La regla nace ejecutable. | El paso junta dos cambios (regla 1). |
| C. Sin regla general: "CreateTask es idempotente" | Nada que hacer en Dune. | El concepto del plan queda como intención, no como regla, y la próxima slice que crea algo puede olvidarlo. |

Recomendación: **A**, con la exclusión de Auth escrita en conventions.md.

## 12. Choques con los docs y con el plan

1. **"Cambia" del plan no nombra el frontend, el smoke ni los e2e.** `plan.md:306` nombra `Infrastructure/`, la slice, la migración y tests. El paso suma un header y respuestas nuevas, entonces la regla 3 obliga a sumar `TasksApi`, `Tasks`, el smoke y el e2e. Tampoco nombra `ApiResults` (422) ni `HttpErrors`. Es lo mismo que pasó en el paso 8 (`progreso.md`, cierre del paso 8: "El paso 8 del plan nombra el frontend, el smoke y los e2e").
2. **"POST de negocio" no está definido.** El plan no dice si Auth entra. Recomiendo excluirlo (sección 11), y el plan no nombra Dune aunque la regla 4 lo exige.
3. **422 no está en el mapeo del catálogo.** `docs/conventions.md:74` lista 400, 401, 403, 404, 409 y 500. El borrador pide 422 para la clave reusada. Hay que cambiar `ApiResults` y esa línea. Si Tobi prefiere no tocar el mapeo, la alternativa es 409 `Error.Conflict`, que se aparta del borrador.
4. **El borrador pide 409 para un pedido en curso; recomiendo esperar.** Es un SHOULD, no un MUST (§2.6 y §2.7). Lo marco; no lo resuelvo.
5. **`Infrastructure/` y los claims de Auth.** `docs/conventions.md:22` dice que `Infrastructure/` no nombra ninguna feature. DUNE0003 permite que nombre `Contracts` (`InfrastructureBoundaryAnalyzer.cs:82-92`), y `architecture.md:43` lo describe como deuda hasta el paso 22. No es una contradicción entre docs (architecture.md lo marca como estado actual, no como objetivo), pero el filtro no debería usar `GetCallerId`. Recomiendo leer `ClaimTypes.NameIdentifier` (sección 3).
6. **"Ningún trabajo programado".** `plan.md:33` impide la limpieza periódica en este paso. Queda como pendiente (sección 7).
7. **`informe-dotnet-empresarial.md:441` dice "Endpoints POST/PUT".** El `PUT` de tareas ya es seguro de repetir desde el paso 8, por la versión y el reintento tolerante (`docs/decisions.tsv:147`). No hace falta clave en el `PUT`. No es un choque; lo aclaro para que no se lea como alcance.

## Decisiones abiertas para Tobi

1. **Contrato.**
   - A. `Idempotency-Key` (borrador IETF, Stripe): ganás el nombre que va a estándar y el que usa el informe del repo; perdés estabilidad (el borrador puede cambiar) y no aparece solo en OpenAPI.
   - B. `Repeatability-*` (OASIS, Azure): ganás un estándar publicado; perdés simplicidad (dos headers, uno con fecha).
   - C. `requestId` en el cuerpo (Google): ganás que se ve en OpenAPI; perdés separación entre transporte y datos.
   - D. `PUT /tasks/{id}` con id del cliente: ganás idempotencia sin tabla; perdés el mecanismo genérico que pide el plan.
   - Recomiendo **A**, con la clave en formato UUID.
2. **Clave obligatoria u opcional.**
   - Opcional: ganás compatibilidad y es lo que hace la industria; perdés protección para un cliente que no la mande.
   - Obligatoria (400 `Idempotency.KeyMissing`): ganás protección total; perdés compatibilidad de v1 y sumás ruido en todos los tests y el smoke.
   - Recomiendo **opcional**, con el frontend mandándola siempre.
3. **Pedido simultáneo con la misma clave.**
   - Esperar: ganás cero código extra y la misma respuesta para los dos; perdés el 409 del borrador y una conexión espera.
   - 409 con `lock_timeout`: ganás lo que pide el borrador y un tope de espera; perdés un código nuevo y el cliente tiene que reintentar.
   - Recomiendo **esperar**.
4. **Qué se guarda.**
   - Solo 2xx, en la misma transacción que la tarea: ganás la invariante "clave si y solo si tarea" sin estado pendiente; perdés que un error no se repite igual.
   - También errores (Stripe): ganás repetir el 500; perdés la transacción única y sumás un estado pendiente que puede quedar trabado.
   - Recomiendo **solo 2xx**.
5. **Marcar la repetición.**
   - `Idempotent-Replayed: true`: ganás un test y un smoke que ven la repetición; perdés pureza (header no estándar).
   - Nada: ganás una respuesta idéntica; perdés visibilidad.
   - Recomiendo **marcarla**.
6. **Estado para la clave reusada con otro cuerpo.**
   - 422 `Idempotency.KeyReused`: ganás lo que pide el borrador; perdés la simplicidad de `ApiResults` (rama nueva para `Error.Custom`, línea nueva en conventions.md).
   - 409 `Error.Conflict`: ganás no tocar el mapeo; perdés coincidencia con el borrador, y 409 ya significa "versión vieja" en `PUT` y "duplicado" en el registro.
   - Recomiendo **422**.
7. **Vencimiento y borrado.**
   - Publicar "al menos 24 h" y no borrar en este paso (pendiente con `Sessions`): ganás un concepto por paso; la tabla crece hasta la limpieza.
   - Vencimiento al leer, borrado oportunista o `BackgroundService`: ganás una tabla acotada; sumás otro concepto o más SQL.
   - Recomiendo **publicar y no borrar**, más la FK con cascada a `UserProfiles` para que borrar una cuenta borre sus respuestas guardadas.
8. **Regla de Dune.**
   - Paso 9.1 con DUNE0016 (todo `MapPost` fuera de Auth lleva `WithIdempotency<T>()`): ganás un paso, un cambio; la regla queda sin cumplir hasta 9.1.
   - Dentro del paso 9: ganás la regla al nacer; perdés "un paso, un cambio".
   - Sin regla: ganás nada que hacer; el concepto queda como intención.
   - Recomiendo **9.1**, con Auth excluido.
9. **Mecanismo propio o librería** (regla 5).
   - Propio: ganás la transacción única con PostgreSQL y ningún paquete; perdés tener que mantenerlo.
   - IdempotentAPI (MIT): ganás código probado por otros; perdés la atomicidad con la tarea (guarda en caché) y sumás un paquete y una caché.
   - Recomiendo **propio**.

## Pendientes fuera de alcance (para `progreso.md`)

- Limpieza periódica de `IdempotencyKeys`, junto con la de `Sessions` (`progreso.md:111`). Paso con trabajo programado.
- El header `Idempotency-Key` no aparece en el documento OpenAPI. Va con el pendiente de OpenAPI y errores.
- Si aparece un cliente que reintenta en paralelo: pasar a 409 con `lock_timeout`.
- La clave del frontend se pierde al recargar la página (FRONT015 prohíbe el storage).
- `RegisterUser` abre su propia transacción: si algún día lleva clave, el filtro tiene que convivir con eso.

## Qué quedó sin comprobar

- No corrí nada del repo: ni tests, ni smoke, ni e2e, ni `dotnet ef` sobre el proyecto real. El experimento usó un modelo mínimo con las mismas piezas (filtro, transacción, `ON CONFLICT`, tabla de claves).
- Que `CreateTask` y el filtro compartan el mismo `AppDbContext` en el repo: lo infiero de la DI (`BackendInfrastructure.cs:25-26`, `TasksFeature.cs:7`).
- Matar el proceso de la API a mitad de un pedido. Probé cortar la conexión de la base, no el proceso.
- Que `api-supported-versions` y otros headers del pipeline salgan igual en una repetición.
- La FK con cascada de `IdempotencyKeys` a `UserProfiles`.
- El e2e con `route.fetch()` seguido de `route.abort()`: la documentación de Playwright no dice si se puede.
- El documento OpenAPI con el filtro.
- Que una búsqueda vacía en `dotnet/aspnetcore` alcance para afirmar que ASP.NET Core 10 no trae idempotencia; ninguna fuente lo contradice.
- El código de estado exacto de Stripe ante una clave con otros parámetros: la documentación dice que "errors", sin el número.
- El caso 5 de la sección 10 con un 400 seguido de un título válido y la misma clave: el experimento probó un 500 seguido del mismo cuerpo (T5) y que un 400 no guarda la clave (T5b), no esa secuencia exacta.
- La aceptación de la clave entre comillas (Structured Fields) y sin comillas (Stripe).

## Fuentes

- https://datatracker.ietf.org/doc/draft-ietf-httpapi-idempotency-key-header/
- https://www.ietf.org/archive/id/draft-ietf-httpapi-idempotency-key-header-08.html
- https://www.rfc-editor.org/rfc/rfc9110.html
- https://docs.stripe.com/api/idempotent_requests
- https://docs.stripe.com/error-low-level
- https://google.aip.dev/155
- https://google.aip.dev/133
- https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md
- https://docs.oasis-open.org/odata/repeatable-requests/v1.0/repeatable-requests-v1.0.html
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/min-api-filters?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/route-handlers?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/routing?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/ef/core/saving/transactions
- https://learn.microsoft.com/en-us/ef/core/querying/sql-queries
- https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.sha256.hashdata
- https://github.com/dotnet/efcore/issues/4526
- https://www.postgresql.org/docs/current/sql-insert.html
- https://www.postgresql.org/docs/current/index-unique-checks.html
- https://www.postgresql.org/docs/current/runtime-config-client.html
- https://www.postgresql.org/docs/current/sql-set.html
- https://www.postgresql.org/docs/current/explicit-locking.html#ADVISORY-LOCKS
- https://www.postgresql.org/docs/current/protocol-flow.html
- https://github.com/ikyriak/IdempotentAPI
- https://angular.dev/guide/http/making-requests
- https://w3c.github.io/webcrypto/
- https://w3c.github.io/webappsec-secure-contexts/
- https://playwright.dev/docs/api/class-route
