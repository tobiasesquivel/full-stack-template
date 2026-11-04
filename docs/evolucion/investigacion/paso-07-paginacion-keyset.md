# Paso 7: paginación por keyset (investigación)

- Fecha: 2026-11-03.
- Alcance: el paso 7 del [plan](../plan.md) (`plan.md:292-295`). La lista de tareas deja de paginar por número de página (offset) y pasa a paginar por posición (keyset). Cambia un contrato HTTP, así que rige la regla 3 de `AGENTS.md`: backend, frontend y tests juntos.
- Decisiones que ya están tomadas y que este informe no reabre: EF Core 10 con `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (`backend/TemplateApp.csproj:35`), PostgreSQL 18 (`backend/Tests/Support/PostgresFixture.cs:24`), versión en la URL `/api/v1` con un solo grupo versionado (`docs/decisions.tsv:110-113`), catálogo de errores con DUNE0014 (`docs/conventions.md:75`), `TimeProvider` para el tiempo (`docs/conventions.md:85`), y la forma de ADR-0004.
- Método: solo fuentes primarias. Son la documentación de PostgreSQL, la de EF Core en Microsoft Learn, la de Npgsql y su código en la etiqueta `v10.0.3`, la documentación XML del paquete instalado, [Google AIP-158](https://google.aip.dev/158) y [AIP-180](https://google.aip.dev/180), las [Azure REST API Guidelines](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md), [use-the-index-luke](https://use-the-index-luke.com/sql/partial-results/fetch-next-page) y angular.dev.
- Evidencia propia: dos experimentos descartables fuera del repo.
  - "Experimento EF": un proyecto de consola en `/tmp/keyset-exp` con `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 y `ToQueryString()`. No necesita base.
  - "Experimento PG": un contenedor `postgres:18` (18.6) descartable, con una tabla igual a `TaskItems`, 200 000 filas repartidas en 100 dueños (2 000 por dueño) y `EXPLAIN (ANALYZE)`.
  - No se tocó código del repo.

## Conclusión

- **El problema es real en esta consulta.** `ListTasks` ordena solo por `CreatedAt` y salta filas con `Skip` (`ListTasks.cs:28-30`). Si se crea una tarea entre la página 1 y la 2, la página 2 repite la última fila de la página 1. El experimento PG lo reprodujo: página 1 `{5,4}`, se inserta la fila 6, la página 2 por offset da `{4,3}` (repite 4) y por keyset da `{3,2}`.
- **Orden:** `CreatedAt DESC, Id DESC`. `CreatedAt` solo no es único: dos tareas pueden tener el mismo instante, y en un test con reloj fijo lo tienen siempre. `Id` (un `Guid`, `TaskItem.cs:9`) desempata.
- **Precisión:** no rompe la igualdad si el cursor guarda el valor que **se leyó de la base**. Npgsql trunca los ticks de .NET (100 ns) a microsegundos al escribir y al mandar parámetros, siempre igual. Lo que sí rompe es un cursor con menos precisión que la base, por ejemplo milisegundos de un `Date` de JavaScript: omite filas. Por eso el cursor lo arma el servidor y el cliente no lo interpreta.
- **Predicado:** `EF.Functions.LessThan(ValueTuple.Create(t.CreatedAt, t.Id), ValueTuple.Create(c, i))` existe en el paquete instalado y el experimento EF lo tradujo a `(t."CreatedAt", t."Id") < (@c, @i)`.
- **Índice:** hoy solo existe `IX_TaskItems_OwnerId`. Sin un índice compuesto `(OwnerId, CreatedAt, Id)`, el keyset arregla los duplicados pero no la velocidad: PostgreSQL igual lee todas las tareas del dueño y las ordena. Con el índice, lee 11 filas. Agregarlo choca con el paso 18 ("Índices medidos, no supuestos"). Queda como decisión de Tobi.
- **Cursor:** opaco, en base64url, con una versión, los ticks UTC de `CreatedAt` y el `Id`. Sin firma ni cifrado: el filtro por dueño sale siempre de la sesión, así que un cursor alterado solo mueve al usuario dentro de su propia lista. Un cursor que no se puede leer da 400 con `TaskErrors.InvalidCursor` (código `Cursor`, que es el nombre del campo).
- **Forma propuesta:** `GET /api/v1/tasks?pageSize=10&cursor=<token>` responde `{ "items": [...], "nextCursor": "<token>" | null }`. Solo hacia adelante en el servidor; `Anterior` se resuelve en el cliente con una pila de cursores. `totalCount` sale o se queda según decida Tobi.
- **Versionado:** cambiar la forma de v1 es un cambio incompatible según AIP-180 y las guías de Microsoft. Hay tres caminos (cambiar v1, crear v2, agregar sin quitar). Queda como decisión de Tobi.
- **Frontend:** `TaskPager` deja de recibir `page`/`totalPages`/`totalCount` y pasa a recibir `hasPrevious`/`hasNext`. `Tasks` guarda la pila de cursores. El e2e de paginación y el helper `listPage` cambian, porque hoy esperan `?page=N`.

## 1. Por qué el offset se degrada y duplica filas, sobre esta consulta

**Respuesta:** `OFFSET` obliga a la base a recorrer y descartar todas las filas anteriores, y numera las filas de nuevo en cada pedido. Si entra una fila nueva arriba, todo se corre un lugar.

- Hoy la slice hace esto (`backend/Features/Tasks/ListTasks/ListTasks.cs:23-33`): filtra por `OwnerId`, cuenta todo con `CountAsync`, ordena por `CreatedAt` descendente y aplica `Skip((page - 1) * pageSize).Take(pageSize)`.
- EF Core lo explica con dos defectos. Primero: "The database must still process the first 20 entries, even if they aren't returned to the application". Segundo: "If any updates occur concurrently, your pagination may end up skipping certain entries or showing them twice" ([EF Core, Pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination)).
- Winand: "the database must count all rows from the beginning until it reaches the requested page" y "the pages drift when inserting new sales because the numbering is always done from scratch" ([use-the-index-luke](https://use-the-index-luke.com/sql/partial-results/fetch-next-page)).
- **En este repo:** la lista va de la tarea más nueva a la más vieja. Crear una tarea la pone arriba de todo. Si estás en la página 1 y otra pestaña crea una tarea, al pulsar `Siguiente` ves otra vez la última tarea de la página 1. Si en cambio se borra una tarea de la página 1, la primera de la página 2 sube a la 1 y nunca la ves.
- Experimento PG (tabla `d`, 5 filas, 2 por página): página 1 = `{5,4}`. Se inserta la fila 6, la más nueva. Página 2 por offset = `{4,3}`: **repite 4**. Página 2 por keyset, desde `(created_at, id) < (fila 4)`, = `{3,2}`: correcta.
- El costo por volumen, en el experimento PG con 2 000 tareas por dueño: la página 181 por offset hizo un `Sort` de 1 810 filas. En este volumen tarda ~1 ms, así que la diferencia de tiempo no se nota todavía. Lo que crece con el volumen es el trabajo: las filas leídas y ordenadas suben con el número de página. [INFERENCIA, a partir del plan del experimento]
- Además, `CountAsync` (`ListTasks.cs:25`) corre en cada página y recorre todas las tareas del dueño.

## 2. Clave de orden y desempate; precisión de `CreatedAt`

**Respuesta:** `ORDER BY CreatedAt DESC, Id DESC`. El cursor guarda `CreatedAt` tal como vino de la base (en ticks UTC) y el `Id`. Así la ida y vuelta es exacta.

**Por qué hace falta el desempate**

- EF Core: "always make sure that your ordering is fully unique. For example, if results are ordered only by date, but there can be multiple results with the same date, then results could be skipped" ([EF Core, Pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination)). Winand: "Paging requires a deterministic sort order" ([use-the-index-luke](https://use-the-index-luke.com/sql/partial-results/fetch-next-page)).
- Hoy el orden es solo `CreatedAt` (`ListTasks.cs:28`). Con el offset de hoy ya puede pasar que dos tareas con el mismo instante salgan en distinto orden en dos pedidos.
- `CreatedAt` sale de `_clock.GetUtcNow()` (`backend/Features/Tasks/CreateTask/CreateTask.cs:40`). En producción el reloj es `TimeProvider.System` (`backend/Infrastructure/DependencyInjection/BackendInfrastructure.cs:30`). Dos altas en el mismo microsegundo son raras pero posibles. En un test con un reloj fijo, **todas** las tareas tienen el mismo instante. [INFERENCIA]
- `Id` es `Guid.NewGuid()` (`CreateTask.cs:36`): único, así que `(CreatedAt, Id)` es único. El orden entre empates es arbitrario pero estable.

**Precisión: .NET 100 ns frente a PostgreSQL 1 µs**

- La columna es `timestamp with time zone` (`backend/Infrastructure/Persistence/Migrations/20261005183500_InitialCreate.cs:211`). PostgreSQL le da una resolución de "1 microsecond" y la guarda en UTC: "All timezone-aware dates and times are stored internally in UTC" ([PostgreSQL, Date/Time Types](https://www.postgresql.org/docs/current/datatype-datetime.html)).
- .NET tiene 100 ns. Npgsql lo reconoce: "the .NET type usually have a higher resolution but a lower range than the PostgreSQL types" ([Npgsql, Date and Time](https://www.npgsql.org/doc/types/datetime.html)).
- Npgsql **trunca** al escribir. En la etiqueta `v10.0.3`, `PgTimestamp.Encode` hace `(value.Ticks - PostgresTimestampOffsetTicks) / 10` con el comentario "we just drop the additional precision" ([PgTimestamp.cs:19-20](https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/Internal/Converters/Temporal/PgTimestamp.cs#L19-L20)). `DateTimeOffset` usa el mismo `Encode` ([DateTimeConverters.cs:37](https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/Internal/Converters/Temporal/DateTimeConverters.cs#L37)). Al leer multiplica por 10 ([PgTimestamp.cs:35](https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/Internal/Converters/Temporal/PgTimestamp.cs#L35)), así que un valor leído de la base siempre tiene ticks múltiplos de 10.
- Npgsql solo escribe un `DateTimeOffset` con offset 0: "only with Offset=0" ([Npgsql, Date and Time](https://www.npgsql.org/doc/types/datetime.html)); si no, lanza `ArgumentException` ([DateTimeConverters.cs:35](https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/Internal/Converters/Temporal/DateTimeConverters.cs#L35)). Al decodificar el cursor hay que armar `new DateTimeOffset(ticks, TimeSpan.Zero)`. Con otro offset, el pedido terminaría en 500.

**Qué rompe la igualdad y qué no** [INFERENCIA, a partir del código de Npgsql citado]

- Cursor con los ticks del valor leído de la base: exacto. Se manda tal cual y se compara con el valor guardado. Es el diseño seguro.
- Cursor con ticks de 100 ns (por ejemplo, el `CreatedAt` de la respuesta del `POST`, que sale del objeto en memoria, `CreateTask.cs:46`): también funciona. Npgsql trunca el parámetro igual que truncó el valor guardado, y la comparación queda bien. Igual no conviene depender de eso.
- Cursor con **menos** precisión que la base, por ejemplo milisegundos: **omite filas**. Si la última fila de la página tiene `12:00:00.123456` y el cursor dice `12:00:00.123`, las filas entre `.123000` y `.123456` quedan "después" del cursor y no salen en la página siguiente. Esto pasaría si el frontend armara el cursor desde `createdAt` con un `Date` de JavaScript, que tiene milisegundos. Por eso el cursor es opaco y lo arma el servidor.
- Hay una rareza fuera de alcance: el `createdAt` de la respuesta 201 puede tener 7 decimales y el mismo dato leído en la lista, 6. No afecta al keyset. Si molesta, se trunca `CreatedAt` a microsegundos en `CreateTask`; eso es otro paso (pendiente para `progreso.md`).

**Diseño propuesto:** orden `CreatedAt DESC, Id DESC`. El cursor guarda `CreatedAt.UtcTicks` de la última fila leída de la base y su `Id`. Al leerlo, se reconstruye con offset cero.

## 3. El predicado en EF Core 10 + Npgsql

**Respuesta:** `EF.Functions.LessThan(ValueTuple.Create(t.CreatedAt, t.Id), ValueTuple.Create(createdAt, id))`. Npgsql lo traduce a una comparación de filas.

- EF Core no lo soporta en general: "EF Core does not currently support expressing this in LINQ queries, this is tracked by #26822" ([EF Core, Pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination)). Npgsql sí, como extensión propia: `EF.Functions.LessThan(ValueTuple.Create(a, b), ValueTuple.Create(c, d))` → `(a, b) < (c, d)` ([Npgsql, Translations](https://www.npgsql.org/efcore/mapping/translations.html)).
- El paquete instalado la tiene. El XML de `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 (en la caché local de NuGet) declara `NpgsqlDbFunctionsExtensions.LessThan(DbFunctions, ITuple, ITuple)` con el resumen "Returns whether the row value represented by a is less than the row value represented by b".
- PostgreSQL compara las filas de izquierda a derecha: "the row elements are compared left-to-right, stopping as soon as an unequal or null pair of elements is found" ([PostgreSQL, Row Constructor Comparison](https://www.postgresql.org/docs/current/functions-comparisons.html#ROW-WISE-COMPARISON)). Las dos columnas son `NOT NULL`, así que no aparece el caso nulo.
- Consulta propuesta (forma, no código final):

  ```csharp
  query.Where(t => t.OwnerId == callerId)
       .Where(t => EF.Functions.LessThan(ValueTuple.Create(t.CreatedAt, t.Id), ValueTuple.Create(after.CreatedAt, after.Id)))
       .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
       .Take(pageSize + 1)
  ```

- SQL que generó el experimento EF:

  ```sql
  SELECT t."Id", t."Title", t."CreatedAt"
  FROM "TaskItems" AS t
  WHERE t."OwnerId" = @o AND (t."CreatedAt", t."Id") < (@c, @i)
  ORDER BY t."CreatedAt" DESC, t."Id" DESC
  LIMIT @p
  ```

- La alternativa portable es `t.CreatedAt < c || (t.CreatedAt == c && t.Id.CompareTo(i) < 0)`. El experimento EF la tradujo a `(t."CreatedAt" < @c OR (t."CreatedAt" = @c AND t."Id" < @i))`. Es la forma de la [documentación de EF Core](https://learn.microsoft.com/en-us/ef/core/querying/pagination). Funciona, pero PostgreSQL usa mejor la forma de filas con un índice (sección 4). Recomiendo la de Npgsql: el repo ya está atado a PostgreSQL. [INFERENCIA]
- `Take(pageSize + 1)`: si vuelve una fila de más, hay página siguiente; se descarta y el cursor sale de la última fila que sí se devuelve. Así no hace falta `COUNT` para saber si hay más. [INFERENCIA]
- No se probó contra la base real del repo con el modelo real: el experimento EF usó un modelo mínimo igual en tabla y columnas.

## 4. Índice

**Respuesta:** la consulta necesita `(OwnerId, CreatedAt, Id)`. Hoy no existe: solo está `IX_TaskItems_OwnerId`. Agregarlo choca con el paso 18.

- Hoy: `builder.HasIndex(t => t.OwnerId)` (`backend/Features/Tasks/Persistence/TaskItemConfiguration.cs:26`) y la migración `IX_TaskItems_OwnerId` (`InitialCreate.cs:279`).
- Un índice ascendente sirve para un orden descendente: "this can satisfy ORDER BY x, y if we scan forward, or ORDER BY x DESC, y DESC if we scan backward" ([PostgreSQL, Indexes and ORDER BY](https://www.postgresql.org/docs/current/indexes-ordering.html)). No hace falta declarar `DESC`.
- PostgreSQL usa la comparación de filas en el índice: "uses them to access the index if there is a corresponding index available" ([use-the-index-luke](https://use-the-index-luke.com/sql/partial-results/fetch-next-page)). EF Core: "make sure to have indexes in place which correspond to your pagination ordering" ([EF Core, Pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination)).
- Experimento PG, 2 000 tareas del dueño:

  | Consulta | Índice | Plan | Filas leídas | Tiempo |
  |---|---|---|---|---|
  | Offset de hoy, página 181 | `OwnerId` | Bitmap Heap Scan + `Sort` | 2 000, ordena 1 810 | 0,94 ms |
  | Keyset | solo `OwnerId` | Bitmap Heap Scan + `Filter` + top-N sort | 2 000 (descarta 500) | 0,73 ms |
  | Keyset | `(OwnerId, CreatedAt, Id)` | `Index Scan Backward`, la fila en `Index Cond` | 11 | 0,05 ms |
  | Offset | `(OwnerId, CreatedAt, Id)` | el planner siguió con Bitmap + `Sort` | 2 000 | 1,13 ms |

- Lectura: **sin el índice compuesto, el keyset arregla los duplicados pero no el trabajo**. Con el índice, la página cuesta lo mismo esté donde esté. Con este volumen los tiempos son chicos; la diferencia está en las filas leídas. [INFERENCIA, a partir del experimento]
- **Choque con el plan.** El paso 7 dice "lectura estable a escala" (`plan.md:293`). El paso 18 dice "Índices medidos, no supuestos" y nombra `TaskItemConfiguration.cs`, una migración y un test con `EXPLAIN` (`plan.md:355-358`). Hay dos lecturas:
  - El paso 7 solo cambia la consulta y el contrato; el índice y su test con `EXPLAIN` quedan en el paso 18. El paso 7 arregla la estabilidad, no la escala.
  - El paso 7 agrega el índice, porque sin él el keyset no cumple su concepto. Entonces el paso 18 pierde su caso principal.
- No lo decido: va en "Decisiones abiertas".
- Si el índice entra, `IX_TaskItems_OwnerId` sobra: el compuesto empieza por `OwnerId` y sirve también para la FK y para `List_IsolatesOwners`. Borrarlo es otra decisión de índice, y cabe en el paso 18. [INFERENCIA]

## 5. Formato del cursor

**Respuesta:** un token opaco en base64url con `versión | UtcTicks de CreatedAt | Id`. Sin firma ni cifrado. Si no se puede leer, 400 con código `Cursor`.

- AIP-158: "Page tokens provided by APIs must be opaque (but URL-safe) strings, and must not be user-parseable". También: "They must not provide any form of authorization to the underlying resources" ([AIP-158](https://google.aip.dev/158)).
- .NET 10 trae `System.Buffers.Text.Base64Url` (`EncodeToString`, `TryDecodeFromChars`), con el alfabeto `-` y `_` ([Microsoft Learn, Base64Url](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.text.base64url)). No hace falta un paquete.
- Contenido propuesto: 1 byte de versión (`1`), 8 bytes con `CreatedAt.UtcTicks` y 16 bytes con el `Guid`. Son 25 bytes, unos 34 caracteres. La versión deja cambiar el formato más adelante sin confundir un token viejo. [INFERENCIA]
- Alternativa más fácil de leer al aprender: un texto `1.<UtcTicks>.<Guid N>` en base64url. Cuesta lo mismo. Cualquiera de las dos cumple "opaco" para el cliente. [INFERENCIA]
- **Validación.** Un cursor es inválido si no es base64url, si su largo o su versión no coinciden, o si los ticks quedan fuera de rango. Entonces la slice devuelve `TaskErrors.InvalidCursor`:
  - `Error.Validation(code: "Cursor", description: "Cursor is invalid.")`. El código es el nombre del campo, como `Page` y `PageSize` hoy (`backend/Features/Tasks/TaskErrors.cs:15-21`). DUNE0014 lo admite: en validación, el código es el campo (`docs/conventions.md:75`).
  - `ApiResults` lo convierte en 400 `ValidationProblem` con `code: "Validation.Failed"` y `errors.Cursor` (`backend/Shared/Http/ApiResults.cs:13-22`).
  - AIP-158 no fija el error para un token roto. Sí pide `INVALID_ARGUMENT` (= 400) cuando cambian los parámetros ([AIP-158](https://google.aip.dev/158)). 400 encaja.
- **¿Firmar o cifrar?** El repo ya tiene Data Protection (`BackendInfrastructure.cs:31,38`), así que firmar es posible.
  - Qué protege: que el usuario no fabrique un cursor. Pero el filtro `OwnerId == callerId` sale de la sesión, no del cursor. Un cursor fabricado solo elige otra posición **dentro de la lista propia**, lo mismo que pedir otra página. No filtra datos de nadie. [INFERENCIA, sobre `ListTasks.cs:23`]
  - Qué cuesta: cada cursor depende de las claves; una rotación o un cambio de `SetApplicationName` invalida los cursores en curso. Un token más largo. Más código y más tests.
  - Qué expone sin firmar: el `Id` y la fecha de creación de la última tarea. El usuario ya los ve en `items`.
  - Recomendación: sin firma ni cifrado. Se valida la forma y nada más. [INFERENCIA]
- **Vencimiento:** AIP-158 permite vencer tokens ("a good rule of thumb is three days"), pero no lo exige ([AIP-158](https://google.aip.dev/158)). Un cursor keyset no se vuelve incorrecto con el tiempo: sigue marcando una posición. No hace falta vencerlo. [INFERENCIA]
- **`pageSize` distinto entre páginas:** AIP-158 pide respetarlo ("the service must honor the new page size"). El cursor no guarda `pageSize`, así que eso sale solo.

## 6. Forma de la respuesta y parámetros

**Respuesta:** `{ items, nextCursor }`. `nextCursor` es `null` en la última página. Solo hacia adelante en el servidor. Los parámetros son `cursor` y `pageSize`.

Lo que dicen las fuentes:

- AIP-158: si no es el final "the API must provide a next_page_token"; si es el final "the next_page_token field must be empty". `total_size` es opcional y puede ser una estimación. `page_size` no es obligatorio, tiene un valor por defecto, y si supera el máximo "the API should coerce down"; un negativo da `INVALID_ARGUMENT` ([AIP-158](https://google.aip.dev/158)).
- Azure: respuesta como objeto con un arreglo (`value`), un `nextLink` absoluto, y "DO NOT return the nextLink field at all when returning the last page" y "DO NOT return the nextLink field with a value of null". Sobre el total: "YOU SHOULD NOT return a count of all objects in the collection as this may be expensive to compute". El parámetro de tamaño es `maxpagesize` ([Azure REST API Guidelines, Collections](https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md)).
- EF Core: el keyset sirve para "next/previous page navigation", no para saltar a una página cualquiera ([EF Core, Pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination)). Winand: hacia atrás hay que invertir la comparación y el orden ([use-the-index-luke](https://use-the-index-luke.com/sql/partial-results/fetch-next-page)).

Propuesta:

| Pieza | Propuesta | Por qué |
|---|---|---|
| Ruta | `GET /api/v1/tasks?pageSize=10&cursor=<token>` | Sin `cursor` = primera página. |
| `pageSize` | Queda igual: 1 a 100, por defecto 20; fuera de rango da 400 `PageSize` (`ListTasksEndpoint.cs:15`, `TaskErrors.cs:19-21`) | AIP-158 prefiere bajar al máximo en vez de dar error. Cambiarlo no es el concepto del paso (regla 7). |
| `page` | Desaparece, y con él `TaskErrors.InvalidPage` | Ya no hay número de página en el servidor. |
| Respuesta | `TaskListResponse(IReadOnlyList<TaskResponse> Items, string? NextCursor)` en `ListTasks/` | La usa una sola slice: queda en la slice (`docs/conventions.md:14`). |
| Fin | `nextCursor: null` | Azure pide omitir el campo; ASP.NET Core escribe `null` por defecto. `null` es más simple de tipar en Angular (`string \| null`). Es una diferencia consciente con Azure. [INFERENCIA] |
| Nombres | `cursor` / `nextCursor` | Coinciden con el nombre del paso. AIP usaría `pageToken` / `nextPageToken`; Azure, `nextLink` con una URL. Las dos son válidas. |
| Hacia atrás | No en el servidor; pila de cursores en el cliente (sección 8) | Una sola consulta, un solo predicado. `prevCursor` duplica la consulta con el orden invertido. |
| `totalCount` | Decisión de Tobi (abajo) | Cuesta un `COUNT` por página. |

Sobre `totalCount`:

- Quitarlo: cada página es una sola consulta y no recorre todas las tareas. El pager ya no puede mostrar "de 2 (11)": muestra "Página 2". Sigue la guía de Azure.
- Dejarlo: el texto del pager se queda casi igual. Cada página suma un `COUNT(*)` sobre todas las tareas del dueño, que crece con el volumen. Es justo lo que el concepto del paso quiere evitar. [INFERENCIA]
- Término medio: un endpoint o un campo aparte solo para el total. Es otro cambio de contrato; no entra en este paso.

## 7. Versionado de la API

**Respuesta:** el cambio es incompatible. Hay tres caminos y cada uno cuesta algo distinto.

- Qué es incompatible: quitar `page`, `totalCount` y `pageSize` de la respuesta y dejar de aceptar `page`. AIP-180: "Removing a component is a backwards incompatible change" y "Renaming a component is semantically equivalent to remove and add". Agregar campos sí es compatible ([AIP-180](https://google.aip.dev/180)).
- Qué está decidido: una sola versión, v1, en un solo grupo (`backend/Infrastructure/Http/VersionedApi.cs:26`, `HasApiVersion(1.0)`). "Versiones distintas por feature se deciden cuando exista una v2" (`docs/decisions.tsv:112`). DUNE0012 hace cumplir el grupo único (`docs/decisions.tsv:113`). La investigación del paso 4 dejó la deprecación y el sunset para "cuando exista v2" (`paso-04-versionado-api.md:16`).
- Quién consume la API hoy: el frontend del mismo repo, por un solo proxy (`frontend/proxy.conf.json`), y el smoke. Se despliegan juntos, y la regla 3 obliga a cambiarlos en el mismo PR.

| Opción | Qué ganás | Qué perdés |
|---|---|---|
| A. Cambiar v1 en el lugar | Un paso chico, un concepto. El único cliente cambia en el mismo PR. | Rompe a cualquier cliente externo que exista o aparezca. No usa el versionado declarado en el paso 4. |
| B. Crear v2 para la lista | Practica el versionado de verdad: v1 sigue con offset, v2 con keyset, y se puede deprecar v1 con fecha. | Choca con el grupo único (`decisions.tsv:112-113`): hay que decidir versiones por feature o por endpoint, cambiar `VersionedApi.cs`, DUNE0012 y sus tests. Son dos conceptos en un paso (regla 1). El offset de v1 sigue vivo, con su defecto. |
| C. Agregar sin quitar, en v1 | Compatible (AIP-180 admite campos nuevos): `cursor` y `nextCursor` se suman a `page` y `totalCount`. | Conviven dos formas de paginar, con dos caminos de código y de tests. Más adelante, quitar `page` igual es incompatible. |

Recomendación para este repo: **A**. El concepto del paso es el keyset, no el versionado, y hoy no hay cliente externo. B pide primero una decisión sobre versiones por feature, que el paso 4 dejó para después; si Tobi la quiere, conviene un paso propio antes del 7. Lo que hace la industria con clientes externos es B o C, con deprecación anunciada. [INFERENCIA]

## 8. Frontend

**Respuesta:** `Anterior` / `Siguiente` con una pila de cursores en `Tasks`. `TaskPager` sigue siendo de presentación, con otras entradas.

Hoy:

- `TasksApi.list(page, pageSize)` manda `page` y `pageSize` (`frontend/src/app/features/tasks/api/tasks-api.ts:12-15`).
- `TaskListResponse` tiene `items`, `totalCount`, `page` y `pageSize` (`api/task-list-response.ts`).
- `Tasks` guarda `page`, `totalCount` y calcula `totalPages` (`tasks/tasks.ts:31-35`). `load()` usa una `Promise` y signals (`tasks.ts:60-73`). Crear vuelve a la página 1 (`tasks.ts:85`). Borrar la única tarea de una página vuelve a la anterior (`tasks.ts:147-149`).
- `TaskPager` recibe `page`, `totalPages`, `totalCount` y `disabled`, y avisa `pageChange` con un número (`task-pager/task-pager.ts:9-13`). Muestra "Página X de Y (N)" (`task-pager.html:10-12`).

Opciones de interfaz:

| Opción | Qué ve el usuario | Qué cambia |
|---|---|---|
| `Anterior` / `Siguiente` con pila de cursores | Lo mismo que hoy, sin el total. "Página 2". | `Tasks` guarda `cursors: (string \| null)[]`: `[null]` al empezar. `Siguiente` apila `nextCursor`; `Anterior` desapila. La página actual es el largo de la pila. |
| "Cargar más" | Una lista que crece hacia abajo. | `items` se acumula. No hay `Anterior`. Cambia el texto, el foco y casi todos los e2e de borrado. |

Recomendación: la pila de cursores. Mantiene el diseño actual de `TaskPager` (paso 0.18), el foco y el diálogo de borrado. EF Core la nombra como el uso típico ("next/previous page navigation", [EF Core, Pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination)). [INFERENCIA]

Reparto entre componentes:

- **`TaskPager` (presentación).** Recibe `page` (el número local), `hasPrevious`, `hasNext` y `disabled`. Avisa `previous` y `next`, sin número. Si `totalCount` se queda, también recibe `totalCount`. No inyecta nada, así que FRONT013 no cambia (`docs/architecture.md:94`).
- **`Tasks` (ruta).** Guarda la pila y el `nextCursor` de la última respuesta. `hasPrevious = computed(() => cursors().length > 1)` y `hasNext = computed(() => nextCursor() !== null)`. Al crear, vuelve la pila a `[null]`. Al borrar la única fila de una página que no es la primera, desapila y recarga.
- **`TasksApi.list(cursor: string | null, pageSize: number)`.** Solo pone `cursor` en `HttpParams` si no es `null`.
- **`TaskListResponse`** (frontend) = `{ items: TaskResponse[]; nextCursor: string | null }`. Mismo nombre que en el backend (`docs/conventions.md:31`).

¿`httpResource` o `resource()`?

- La guía del repo dice que la lectura async usa `resource()` y `httpResource()` (`docs/evolucion/informe-angular-moderno.md:59`). `httpResource` vuelve a pedir "whenever one of the signals it depends on changes" y cancela el pedido anterior ([angular.dev, httpResource](https://angular.dev/guide/http/http-resource)). `resource()` tiene `reload()` ([angular.dev, resource](https://angular.dev/guide/signals/resource)).
- Con la pila, la lectura dependería de `currentCursor = computed(() => cursors().at(-1))`. Encaja bien.
- Pero hoy `Tasks` no usa ninguno: usa `load()` con `Promise`. Pasar a `resource()` quedó como paso opcional "D" en `informe-componentes-angular.md:217-219`, porque "puede cambiar comportamiento". Hacerlo en el paso 7 suma un segundo concepto (regla 1). Recomiendo mantener `load()` y anotar el `resource()` como pendiente. [INFERENCIA]

Spec de Vitest (`task-pager/task-pager.spec.ts`):

- Cambian los cuatro casos. El texto pasa a "Página 2" (o sigue con total si se queda). `Anterior` deshabilitado si `hasPrevious` es `false`; `Siguiente` deshabilitado si `hasNext` es `false`; los dos con `disabled`. Un clic avisa `previous` o `next`.
- La lógica de la pila vive en `Tasks`, que no tiene spec. Si se quiere probarla en Vitest, conviene sacarla a una función pura en la carpeta de la feature. Es una decisión de forma; no es necesaria. [INFERENCIA]

## 9. Tests

**Backend (`backend/Features/Tasks/ListTasks/ListTasksTests.cs`)**

- **Insertar entre dos páginas.** Alice crea 5 tareas. Pide `pageSize=2` → página 1 y `nextCursor`. Crea 2 tareas más. Pide la página 2 con el cursor, y así hasta `nextCursor: null`. Afirma:
  - ningún `id` se repite entre páginas;
  - la unión de las páginas es exactamente las 5 tareas originales;
  - las 2 nuevas no aparecen (están "antes" del cursor).
  - Con el offset de hoy este test da rojo: repite 2 filas. Sirve como rojo antes del cambio (regla 2).
- **Empates en `CreatedAt`.** Un reloj fijo para todas las altas, así que todas las tareas tienen el mismo instante y el orden depende solo del `Id`.
  - `ApiFixture.CreateIsolatedClient(Action<IServiceCollection>, out IServiceProvider)` ya permite reemplazar servicios (`backend/Tests/Support/ApiFixture.cs:33-34`).
  - El repo no tiene `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`). Sumarlo es una herramienta nueva (regla 5). Alternativa sin paquete: una clase de prueba que hereda de `TimeProvider` y devuelve un instante fijo en `GetUtcNow()`. `TimeProvider` es abstracta y `GetUtcNow` es virtual. [INFERENCIA]
  - Afirma lo mismo que el caso anterior: sin repetidos ni faltantes en 5 tareas, `pageSize=2`.
  - El orden esperado no se calcula en .NET. `Guid.CompareTo` y el `uuid` de PostgreSQL pueden ordenar distinto. Se compara contra una lectura de una sola página (`pageSize=100`). [INFERENCIA, no verificado contra la fuente de PostgreSQL]
- **Cursor inválido.** `cursor=xyz`, un base64url válido pero corto, y una versión desconocida → 400, `code: "Validation.Failed"` y `errors.Cursor`. Se puede usar `ProblemAssert` (`backend/Tests/Support/ProblemAssert.cs`).
- **Límites de `pageSize`.** 0 y 101 siguen dando 400 (como hoy en `ListTasksTests.cs`). Desaparece el caso `page=0`.
- **Forma.** La primera página trae `items` y `nextCursor`; la última, `nextCursor: null`. `List_IsolatesOwners` cambia `totalCount` por `nextCursor: null`, salvo que `totalCount` se quede.
- **Índice** (solo si entra en el paso 7): un test con `EXPLAIN` que afirma `Index Scan` sobre el índice compuesto. Es lo que el plan pone en el paso 18.

**Smoke (`.agents/skills/verify-template/scripts/smoke-api.mjs`)**

- La lista de claves observadas incluye `totalCount`, `page` y `pageSize` (`smoke-api.mjs:64`): suma `nextCursor`.
- Las líneas 186-197 piden `?page=1&pageSize=2` y `?page=2&pageSize=2` y afirman `totalCount`, `page` y `pageSize`. Pasan a seguir `nextCursor`. La aserción de "3 ids distintos" se queda. El 400 de `pageSize=101` con `Validation.Failed` se queda.
- `features/tasks.md:34,36` describe el smoke y la paginación de la UI: se actualiza el texto.

**E2E (`frontend/e2e/`)**

- `listPage(page, number)` espera `searchParams.get('page') === String(number)` (`frontend/e2e/support/app.ts:81-88`). Deja de servir: tiene que esperar "sin `cursor`" para la primera página y "con `cursor`" para las demás.
- `tasks.spec.ts:113-131` espera "Página 1 de 2 (11)" y "Página 2 de 2 (11)". Las líneas 192-204 y 244 esperan "Página 2 de 2 (11)", "Página 1 de 1 (10)" y "Página 1 de 1 (9)". Cambian según la decisión sobre `totalCount`.
- El e2e corre una sola vez, al cerrar el paso, porque cambia el frontend y el contrato (`AGENTS.md`, "Verificación").

**Navegador (`verify-template` drive):** 11 tareas, `Siguiente`, otra pestaña crea una tarea, `Siguiente`/`Anterior` sin repetir. Es el caso del concepto, visto por el usuario.

## 10. Tamaño del paso y choques con los docs

**Tamaño.** El paso toca la slice, `TaskErrors`, el endpoint, la respuesta, `TasksApi`, `TaskListResponse` del frontend, `TaskPager` y su spec, `Tasks`, el smoke, dos archivos de e2e y `features/tasks.md`. Es grande pero es un solo cambio observable: la lista pagina por posición. Si además entra el índice, suma una migración y un test con `EXPLAIN`. Si entra v2, suma un segundo concepto. [INFERENCIA]

**Choques encontrados**

1. **Índice: paso 7 o paso 18.** Sin índice compuesto el keyset no cumple "a escala" (experimento PG). El paso 18 reserva el índice y su medición (`plan.md:355-358`). Ver sección 4.
2. **"Cambia: Contracts" en el plan.** `plan.md:294` dice que el paso cambia `Contracts`. Desde el paso 6.3, `Contracts/` es solo lo que usa otra feature (`docs/conventions.md:14`, ADR-0004 punto 5). `TaskListResponse` vive en `ListTasks/` y ninguna otra feature lista tareas. Lo coherente con los docs es **no** tocar `Contracts/`. El plan quedó desactualizado; conviene corregir esa palabra en el plan. [INFERENCIA]
3. **Versionado.** Cambiar v1 en el lugar usa una regla que el paso 4 no escribió: cuándo un cambio incompatible exige v2. Crear v2 choca con el grupo único (`decisions.tsv:112-113`) y con DUNE0012. Ver sección 7.
4. **Fase del paso.** `progreso.md:7` dice "Fase 3, paso 7", pero `plan.md:240` pone el paso 7 en la Fase 2. Es un error de texto. Lo anoto; no cambia nada.
5. **`resource()`.** La guía de Angular del repo prefiere `resource()`/`httpResource()` para leer (`informe-angular-moderno.md:59`), pero el cambio quedó como paso opcional aparte (`informe-componentes-angular.md:217`). Ver sección 8.
6. **`nextCursor: null` frente a Azure.** Azure pide omitir `nextLink` en la última página. La propuesta usa `null`. Es una diferencia de forma, no un choque con los docs del repo.

## Decisiones abiertas para Tobi

1. **Versionado del cambio.**
   - A. Cambiar v1 en el lugar: ganás un paso chico y un concepto; perdés compatibilidad con clientes externos (hoy no hay).
   - B. Crear v2: ganás práctica real de versionado y deprecación; perdés tamaño (otro concepto, cambia `VersionedApi.cs` y DUNE0012) y mantenés el offset vivo en v1.
   - C. Agregar sin quitar: ganás compatibilidad; perdés simplicidad, con dos formas de paginar.
   - Recomiendo **A**, porque el único cliente cambia en el mismo PR. Si querés B, mejor un paso propio antes del 7.
2. **Índice compuesto en el paso 7 o en el 18.**
   - En el 7: ganás que el paso cumpla "a escala" de punta a punta; perdés el caso principal del paso 18, o lo convertís en "medir lo que ya se agregó".
   - En el 18: ganás pasos limpios (7 = estabilidad, 18 = rendimiento medido); perdés que el paso 7 todavía no escala, y el cierre tiene que decirlo.
   - Recomiendo **dejarlo en el 18** y que el cierre del 7 lo diga, porque el plan ya lo separó así y el paso 18 tiene el test con `EXPLAIN`. Es una decisión de diseño del plan, no técnica.
3. **`totalCount`.**
   - Quitarlo: ganás una consulta por página y seguís la guía de Azure; perdés el "de Y (N)" del pager, y cambian los e2e.
   - Dejarlo: ganás el texto actual; perdés escala, con un `COUNT` por página que crece con el volumen.
   - Recomiendo **quitarlo**, porque el concepto del paso es la lectura que no crece con el volumen.
4. **Reloj fijo en los tests.**
   - `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`, de Microsoft): ganás la herramienta estándar; perdés un paquete nuevo de test (regla 5).
   - Una clase de prueba propia que hereda de `TimeProvider`: ganás cero dependencias; perdés unas líneas de código propio.
   - Recomiendo **la clase propia**: el test solo necesita un instante fijo.

No pongo como decisión el nombre de los parámetros (`cursor` frente a `pageToken`), la firma del cursor ni la interfaz "Cargar más". Las tres tienen recomendación clara arriba. Si Tobi quiere otra cosa, se cambia sin costo extra.

## Qué quedó sin comprobar

- La consulta final con el modelo real del repo y `TaskResponses.Projection`. El experimento EF usó un modelo mínimo con la misma tabla y las mismas columnas.
- El plan con el índice compuesto sobre la base del repo y su volumen real. El experimento PG usó datos sintéticos (2 000 tareas por dueño) y una sola corrida de cada consulta.
- Que `Guid.CompareTo` y el orden de `uuid` de PostgreSQL difieren. Lo afirmo como riesgo; no leí la fuente de `uuid_cmp` ni lo probé. El test propuesto no depende de eso.
- La documentación de Npgsql no dice desde qué versión existe la comparación de filas. Lo comprobé solo en el paquete instalado 10.0.3 (XML del paquete y experimento EF).
- La resolución real de `TimeProvider.System` en Windows y en el contenedor Linux. La probabilidad de dos altas en el mismo microsegundo es una inferencia.
- Si Data Protection rota claves en este repo con una frecuencia que importe para un cursor firmado. No aplica si se acepta la recomendación de no firmar.
- No corrí los tests, el smoke ni los e2e del repo: este informe no cambia código.

## Fuentes

- https://learn.microsoft.com/en-us/ef/core/querying/pagination
- https://www.npgsql.org/efcore/mapping/translations.html
- https://www.npgsql.org/doc/types/datetime.html
- https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/Internal/Converters/Temporal/PgTimestamp.cs
- https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/Internal/Converters/Temporal/DateTimeConverters.cs
- https://www.postgresql.org/docs/current/datatype-datetime.html
- https://www.postgresql.org/docs/current/functions-comparisons.html#ROW-WISE-COMPARISON
- https://www.postgresql.org/docs/current/indexes-ordering.html
- https://use-the-index-luke.com/sql/partial-results/fetch-next-page
- https://google.aip.dev/158
- https://google.aip.dev/180
- https://github.com/microsoft/api-guidelines/blob/vNext/azure/Guidelines.md
- https://learn.microsoft.com/en-us/dotnet/api/system.buffers.text.base64url
- https://angular.dev/guide/http/http-resource
- https://angular.dev/guide/signals/resource
